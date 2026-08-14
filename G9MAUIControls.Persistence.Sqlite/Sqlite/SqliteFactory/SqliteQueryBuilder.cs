using SQLite;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     Core fluent query builder that can build SELECT, UPDATE, and DELETE SQL.
///     This type is AOT-safe and does not compile expressions at runtime.
/// </summary>
public sealed partial class SqliteQueryBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    private static readonly ConcurrentDictionary<(Type EntityType, string PropertyName), bool> LocalizedColumnCache =
        new();

    private static readonly ConcurrentDictionary<Type, EntityColumnMap> EntityColumnMapCache = new();

    #region SqlVisitor - Expression tree to SQL

    private sealed class SqlVisitor(
        Dictionary<ParameterExpression, (string Table, Type Type)> paramMap,
        string? culture,
        Func<string, Type, bool> isLocalized,
        Func<string, Type, string> resolveColumnName,
        Func<string, Type, bool> isGuidStringIdColumn,
        bool hasJoins)
    {
        public List<object> Parameters { get; } = [];

        /// <summary>Converts a supported expression node into an equivalent SQL fragment.</summary>
        public string Visit(Expression node)
        {
            return node switch
            {
                BinaryExpression b => VisitBinary(b),
                UnaryExpression u => VisitUnary(u),
                MemberExpression m => VisitMember(m),
                ConstantExpression c => VisitConstant(c),
                MethodCallExpression mc => VisitMethodCall(mc),
                _ => throw new NotSupportedException(
                    $"Expression type {node.NodeType} ({node.GetType().Name}) is not supported.")
            };
        }

        private string VisitBinary(BinaryExpression b)
        {
            if (IsNullComparison(b, out var memberSql, out var isEqual))
            {
                return isEqual ? $"{memberSql} IS NULL" : $"{memberSql} IS NOT NULL";
            }

            var op = b.NodeType switch
            {
                ExpressionType.Equal => "=",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.LessThan => "<",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "AND",
                ExpressionType.OrElse => "OR",
                ExpressionType.Add => "+",
                ExpressionType.Subtract => "-",
                ExpressionType.Multiply => "*",
                ExpressionType.Divide => "/",
                ExpressionType.Modulo => "%",
                _ => throw new NotSupportedException($"Binary operator {b.NodeType} not supported.")
            };

            if (IsValueComparison(b.NodeType) &&
                TryBuildGuidStringIdValueComparison(b.Left, b.Right, op, out var normalizedSql))
            {
                return normalizedSql;
            }

            var left = Visit(b.Left);
            var right = Visit(b.Right);

            return b.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse
                ? $"({left} {op} {right})"
                : $"{left} {op} {right}";
        }

        private string VisitUnary(UnaryExpression u)
        {
            return u.NodeType switch
            {
                ExpressionType.Not when u.Operand is MemberExpression m && m.Type == typeof(bool)
                    => $"{VisitMember(m)} = 0",
                ExpressionType.Not => $"NOT ({Visit(u.Operand)})",
                ExpressionType.Convert => Visit(u.Operand),
                ExpressionType.Negate => $"-({Visit(u.Operand)})",
                _ => throw new NotSupportedException($"Unary operator {u.NodeType} not supported.")
            };
        }

        private string VisitMember(MemberExpression m)
        {
            if (m.Expression is ParameterExpression param && paramMap.TryGetValue(param, out var info))
            {
                var propName = m.Member.Name;
                var columnName = resolveColumnName(propName, info.Type);
                var isLoc = culture != null && isLocalized(propName, info.Type);
                var col = hasJoins ? $"{info.Table}.{QuoteIdentifier(columnName)}" : QuoteIdentifier(columnName);
                return isLoc ? BuildLocalizedValueExpression(col, culture!) : col;
            }

            var value = Evaluate(m);
            AddParam(value);
            return "?";
        }

        private string VisitConstant(ConstantExpression c)
        {
            if (c.Value is null)
            {
                return "NULL";
            }

            AddParam(c.Value);
            return "?";
        }

        private string VisitMethodCall(MethodCallExpression mc)
        {
            var method = mc.Method;

            if (method.DeclaringType == typeof(SqliteAggregateMaker))
            {
                return method.Name switch
                {
                    "Count" when mc.Arguments.Count == 0 => "COUNT(*)",
                    "Count" => $"COUNT({Visit(mc.Arguments[0])})",
                    "CountWhen" when mc.Arguments.Count == 1
                        => $"COALESCE(SUM(CASE WHEN ({Visit(mc.Arguments[0])}) THEN 1 ELSE 0 END), 0)",
                    "Sum" => $"SUM({Visit(mc.Arguments[0])})",
                    "Avg" => $"AVG({Visit(mc.Arguments[0])})",
                    "Min" => $"MIN({Visit(mc.Arguments[0])})",
                    "Max" => $"MAX({Visit(mc.Arguments[0])})",
                    "Coalesce" => $"COALESCE({Visit(mc.Arguments[0])}, {Visit(mc.Arguments[1])})",
                    _ => throw new NotSupportedException($"SqliteAggregateMaker.{method.Name} not supported.")
                };
            }

            if (mc.Object != null && method.DeclaringType == typeof(string))
            {
                var col = Visit(mc.Object);
                var rawArg = Evaluate(mc.Arguments[0])?.ToString() ?? "";
                var escaped = EscapeLike(rawArg);
                return method.Name switch
                {
                    "Contains" => LikeParam(col, $"%{escaped}%"),
                    "StartsWith" => LikeParam(col, $"{escaped}%"),
                    "EndsWith" => LikeParam(col, $"%{escaped}"),
                    _ => throw new NotSupportedException($"string.{method.Name} not supported in SQL expressions.")
                };
            }

            if (method.Name == "IsNullOrEmpty" && method.DeclaringType == typeof(string) && mc.Arguments.Count == 1)
            {
                var col = Visit(mc.Arguments[0]);
                return $"({col} IS NULL OR {col} = '')";
            }

            if (method.Name == "Contains" && mc.Object != null && mc.Arguments.Count == 1
                && mc.Arguments[0] is MemberExpression)
            {
                var collection = Evaluate(mc.Object) as IEnumerable;
                if (TryGetGuidStringIdColumn(mc.Arguments[0], out var idColumn))
                {
                    return BuildIn(idColumn, collection, true);
                }

                var col = Visit(mc.Arguments[0]);
                return BuildIn(col, collection);
            }

            if (method.Name == "Contains" && method.DeclaringType == typeof(Enumerable)
                                          && mc.Arguments.Count == 2)
            {
                var collection = Evaluate(mc.Arguments[0]) as IEnumerable;
                if (TryGetGuidStringIdColumn(mc.Arguments[1], out var idColumn))
                {
                    return BuildIn(idColumn, collection, true);
                }

                var col = Visit(mc.Arguments[1]);
                return BuildIn(col, collection);
            }

            throw new NotSupportedException(
                $"Method {method.DeclaringType?.Name}.{method.Name} is not supported in SQL expressions.");
        }

        #region Helpers

        private bool IsNullComparison(BinaryExpression b, out string memberSql, out bool isEqual)
        {
            memberSql = "";
            isEqual = b.NodeType == ExpressionType.Equal;
            if (b.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
            {
                return false;
            }

            Expression? member = null;
            if (IsNull(b.Right))
            {
                member = b.Left;
            }
            else if (IsNull(b.Left))
            {
                member = b.Right;
            }

            if (member == null)
            {
                return false;
            }

            memberSql = Visit(member);
            return true;

            static bool IsNull(Expression e)
            {
                return e is ConstantExpression { Value: null }
                       || (e is UnaryExpression { NodeType: ExpressionType.Convert } u
                           && u.Operand is ConstantExpression { Value: null });
            }
        }

        private bool TryBuildGuidStringIdValueComparison(
            Expression leftExpression,
            Expression rightExpression,
            string op,
            out string sql)
        {
            sql = string.Empty;
            if (TryGetGuidStringIdColumn(leftExpression, out var leftColumn) &&
                !IsMappedColumnMember(rightExpression) &&
                TryEvaluate(rightExpression, out var rightValue))
            {
                AddParam(rightValue, true);
                sql = $"{leftColumn} {op} ?";
                return true;
            }

            if (TryGetGuidStringIdColumn(rightExpression, out var rightColumn) &&
                !IsMappedColumnMember(leftExpression) &&
                TryEvaluate(leftExpression, out var leftValue))
            {
                AddParam(leftValue, true);
                sql = $"? {op} {rightColumn}";
                return true;
            }

            return false;
        }

        private bool TryGetGuidStringIdColumn(Expression expression, out string columnSql)
        {
            expression = UnwrapConvert(expression);
            columnSql = string.Empty;

            if (expression is not MemberExpression memberExpression ||
                memberExpression.Expression is not ParameterExpression parameter ||
                !paramMap.TryGetValue(parameter, out var info) ||
                !isGuidStringIdColumn(memberExpression.Member.Name, info.Type))
            {
                return false;
            }

            columnSql = VisitMember(memberExpression);
            return true;
        }

        private bool IsMappedColumnMember(Expression expression)
        {
            expression = UnwrapConvert(expression);
            return expression is MemberExpression memberExpression &&
                   memberExpression.Expression is ParameterExpression parameter &&
                   paramMap.ContainsKey(parameter);
        }

        private static bool TryEvaluate(Expression expression, out object? value)
        {
            try
            {
                value = Evaluate(expression);
                return true;
            }
            catch (NotSupportedException)
            {
                value = null;
                return false;
            }
        }

        private static bool IsValueComparison(ExpressionType nodeType)
        {
            return nodeType is ExpressionType.Equal
                or ExpressionType.NotEqual
                or ExpressionType.GreaterThan
                or ExpressionType.LessThan
                or ExpressionType.GreaterThanOrEqual
                or ExpressionType.LessThanOrEqual;
        }

        private void AddParam(object? value, bool normalizeGuidStringId = false)
        {
            if (normalizeGuidStringId)
            {
                value = SqliteGuidStringNormalizer.NormalizeIdLikeValue(value);
            }

            Parameters.Add(SqliteQueryFactory.NormalizeParam(value));
        }

        private static string EscapeLike(string value)
        {
            return value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        }

        private string LikeParam(string column, string pattern)
        {
            Parameters.Add(pattern);
            return $@"{column} LIKE ? ESCAPE '\'";
        }

        private string BuildIn(string column, IEnumerable? values, bool normalizeGuidStringIds = false)
        {
            if (values == null)
            {
                return "0 = 1";
            }

            var items = new List<object>();
            foreach (var v in values)
            {
                items.Add(v!);
            }

            if (items.Count == 0)
            {
                return "0 = 1";
            }

            var sb = new StringBuilder();
            sb.Append(column).Append(" IN (");
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append('?');
                AddParam(items[i], normalizeGuidStringIds);
            }

            sb.Append(')');
            return sb.ToString();
        }

        /// <summary>
        ///     Evaluates a non-parameter expression to its runtime value.
        ///     AOT-safe: uses FieldInfo/PropertyInfo.GetValue only.
        /// </summary>
        private static object? Evaluate(Expression expr)
        {
            return expr switch
            {
                ConstantExpression c => c.Value,
                MemberExpression m => m.Member switch
                {
                    FieldInfo fi => fi.GetValue(m.Expression != null ? Evaluate(m.Expression) : null),
                    PropertyInfo pi => pi.GetValue(m.Expression != null ? Evaluate(m.Expression) : null),
                    _ => throw new NotSupportedException($"Member type {m.Member.GetType().Name} not supported.")
                },
                UnaryExpression { NodeType: ExpressionType.Convert } u => Evaluate(u.Operand),
                MethodCallExpression mc => mc.Method.Invoke(
                    mc.Object != null ? Evaluate(mc.Object) : null,
                    mc.Arguments.Select(Evaluate).ToArray()),
                _ => throw new NotSupportedException(
                    $"Cannot evaluate expression of type {expr.NodeType}. Extract the value into a local variable.")
            };
        }

        private static Expression UnwrapConvert(Expression expression)
        {
            while (expression is UnaryExpression
                   {
                       NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
                   } unary)
            {
                expression = unary.Operand;
            }

            return expression;
        }

        #endregion
    }

    #endregion

    #region Internal Types

    private readonly record struct JoinClause(string JoinType, string TableName, string OnSql, object[] OnParams);

    private readonly record struct SetClause(string Column, object? Value);

    private readonly record struct EntityPropertyMap(
        string ColumnName,
        bool IsLocalized,
        bool IsIgnored,
        bool IsGuidStringId);

    private sealed class EntityColumnMap
    {
        public required string[] SelectablePropertyNames { get; init; }
        public required Dictionary<string, EntityPropertyMap> PropertyMaps { get; init; }
    }

    #endregion

    #region State

    private readonly string _rootTable = SqliteQueryFactory.GetTableName(typeof(T));

    private readonly Dictionary<Type, string> _knownTables =
        new() { [typeof(T)] = SqliteQueryFactory.GetTableName(typeof(T)) };

    private readonly HashSet<(Type EntityType, string PropertyName)> _localizedOverrides = [];
    private readonly List<string> _selectParts = [];
    private readonly List<object> _selectParams = [];
    private readonly List<JoinClause> _joins = [];
    private readonly List<string> _whereParts = [];
    private readonly List<object> _whereParams = [];
    private readonly List<string> _orderByParts = [];
    private readonly List<string> _groupByParts = [];
    private string? _havingSql;
    private readonly List<object> _havingParams = [];
    private readonly List<SetClause> _setClauses = [];
    private int? _limit;
    private int? _offset;
    private bool _distinct;
    private string? _culture;

    private bool HasJoins => _joins.Count > 0;

    #endregion
}
