using SQLite;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

            var isLogical = b.NodeType is ExpressionType.AndAlso or ExpressionType.OrElse;

            var leftStart = Parameters.Count;
            var left = VisitOperand(b.Left, isLogical);
            var rightStart = Parameters.Count;
            var right = VisitOperand(b.Right, isLogical);

            if (b.NodeType is ExpressionType.Equal or ExpressionType.NotEqual &&
                TryBuildNullAwareEquality(
                    b.NodeType == ExpressionType.Equal, left, leftStart, right, rightStart, out var nullAwareSql))
            {
                return nullAwareSql;
            }

            // Logical AND arithmetic nodes carry their own parentheses, so the C# tree's grouping survives
            // whatever they end up nested in. Arithmetic used to be emitted bare, which let SQL's operator
            // precedence regroup it: (A + B) * C became A + B * C. A comparison stays bare at the top level
            // — the common case reads the same as before — and is wrapped by VisitOperand when it is itself
            // an operand.
            return isLogical || IsArithmetic(b.NodeType)
                ? $"({left} {op} {right})"
                : $"{left} {op} {right}";
        }

        private string VisitOperand(Expression operand, bool parentIsLogical)
        {
            var sql = Visit(operand);

            // Under AND/OR a comparison needs no help: every comparison operator binds tighter than both.
            // Under another comparison or arithmetic it does — (a == b) != c must not become a = b != c.
            return !parentIsLogical &&
                   UnwrapConvert(operand) is BinaryExpression nested &&
                   IsValueComparison(nested.NodeType)
                ? $"({sql})"
                : sql;
        }

        private static bool IsArithmetic(ExpressionType nodeType)
        {
            return nodeType is ExpressionType.Add
                or ExpressionType.Subtract
                or ExpressionType.Multiply
                or ExpressionType.Divide
                or ExpressionType.Modulo;
        }

        /// <summary>
        ///     Gives <c>==</c> / <c>!=</c> against an EVALUATED value the meaning the C# predicate has,
        ///     where SQL's three-valued logic would otherwise disagree.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Only a literal <c>null</c> used to be recognised. A CAPTURED null —
        ///         <c>Where(x =&gt; x.ParentId == parentId)</c> with <c>parentId == null</c> — was bound as a
        ///         parameter, producing <c>ParentId = NULL</c>, which is never true: the query for "rows
        ///         with no parent" returned nothing. Likewise <c>col != ?</c> is NULL, not true, for a row
        ///         whose column is NULL, so <c>!=</c> silently dropped exactly the rows C# would keep.
        ///     </para>
        ///     <para>
        ///         A side counts as a value when it rendered as a single <c>?</c> and contributed exactly one
        ///         parameter — which is what <see cref="VisitMember" /> and <see cref="VisitConstant" /> do
        ///         for anything that is not a mapped column. Column-to-column comparisons (join conditions)
        ///         are deliberately left alone, and so is <c>==</c> against a non-null value: that SQL was
        ///         already right and is emitted byte-for-byte as before.
        ///     </para>
        /// </remarks>
        private bool TryBuildNullAwareEquality(
            bool isEqual,
            string left,
            int leftStart,
            string right,
            int rightStart,
            out string sql)
        {
            sql = string.Empty;

            var leftIsValue = left == "?" && rightStart == leftStart + 1;
            var rightIsValue = right == "?" && Parameters.Count == rightStart + 1;

            if (leftIsValue && rightIsValue)
            {
                // Two values and no column. `=` is only wrong here when a null is involved; IS / IS NOT are
                // SQLite's null-safe forms and agree with C# for every combination.
                if (Parameters[leftStart] is not null && Parameters[rightStart] is not null)
                {
                    return false;
                }

                sql = isEqual ? "? IS ?" : "? IS NOT ?";
                return true;
            }

            if (!leftIsValue && !rightIsValue)
            {
                return false;
            }

            var valueIndex = rightIsValue ? rightStart : leftStart;
            var other = rightIsValue ? left : right;

            if (Parameters[valueIndex] is null)
            {
                // The value was bound before it was known to be null; IS NULL takes no parameter.
                Parameters.RemoveAt(valueIndex);
                sql = isEqual ? $"{other} IS NULL" : $"{other} IS NOT NULL";
                return true;
            }

            if (isEqual)
            {
                return false;
            }

            // The other side now appears twice in the SQL, so any parameters IT bound (arithmetic such as
            // x.A + offset) must be bound twice too, in the order the placeholders appear.
            var otherParameters = rightIsValue
                ? Parameters.GetRange(leftStart, rightStart - leftStart)
                : Parameters.GetRange(rightStart, Parameters.Count - rightStart);

            Parameters.AddRange(otherParameters);
            sql = $"({left} != {right} OR {other} IS NULL)";
            return true;
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

            // C# 14 "first-class spans": `array.Contains(x.Id)` no longer binds to Enumerable.Contains but
            // to MemoryExtensions.Contains(ReadOnlySpan<T>, T), with the array reaching it through an
            // implicit span conversion. Same meaning, different tree — and it was unsupported, so every
            // consumer had to write ((IEnumerable<string>)ids).Contains(x.Id) instead. The conversion node
            // is peeled off and the collection underneath evaluated: a span cannot be produced by
            // reflection at all (it is a ref struct), so evaluating the conversion itself is not an option.
            if (method.Name == "Contains" && method.DeclaringType == typeof(MemoryExtensions)
                                          && mc.Object == null && mc.Arguments.Count == 2
                                          && TryUnwrapSpanConversion(mc.Arguments[0], out var spanSource))
            {
                // A string converts to ReadOnlySpan<char>, but "abc".Contains(x.Letter) is a character
                // test, not an id list — leave that to the not-supported error below.
                if (Evaluate(spanSource) is IEnumerable collection and not string)
                {
                    if (TryGetGuidStringIdColumn(mc.Arguments[1], out var idColumn))
                    {
                        return BuildIn(idColumn, collection, true);
                    }

                    var col = Visit(mc.Arguments[1]);
                    return BuildIn(col, collection);
                }
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
                sql = BuildGuidStringIdValueComparison(leftColumn, op, rightValue, true);
                return true;
            }

            if (TryGetGuidStringIdColumn(rightExpression, out var rightColumn) &&
                !IsMappedColumnMember(leftExpression) &&
                TryEvaluate(leftExpression, out var leftValue))
            {
                sql = BuildGuidStringIdValueComparison(rightColumn, op, leftValue, false);
                return true;
            }

            return false;
        }

        // The id-column twin of TryBuildNullAwareEquality — see there for why. An id column renders as a
        // bare column reference and binds nothing, so repeating it needs no parameter bookkeeping.
        private string BuildGuidStringIdValueComparison(string column, string op, object? value, bool columnOnLeft)
        {
            if (value is null && op is "=" or "!=")
            {
                return op == "=" ? $"{column} IS NULL" : $"{column} IS NOT NULL";
            }

            AddParam(value, true);
            var comparison = columnOnLeft ? $"{column} {op} ?" : $"? {op} {column}";
            return op == "!=" ? $"({comparison} OR {column} IS NULL)" : comparison;
        }

        private static bool TryUnwrapSpanConversion(Expression expression, out Expression source)
        {
            source = expression;
            var unwrapped = false;

            // A loop because T[] -> Span<T> -> ReadOnlySpan<T> is two conversions.
            while (true)
            {
                switch (source)
                {
                    // The shape the compiler emits: a call to Span<T>/ReadOnlySpan<T>.op_Implicit.
                    case MethodCallExpression { Method.Name: "op_Implicit", Object: null, Arguments.Count: 1 } call
                        when IsSpanType(call.Type):
                        source = call.Arguments[0];
                        unwrapped = true;
                        continue;

                    // The shape a hand-built tree (Expression.Convert) produces for the same conversion.
                    case UnaryExpression { NodeType: ExpressionType.Convert } convert when IsSpanType(convert.Type):
                        source = convert.Operand;
                        unwrapped = true;
                        continue;

                    default:
                        return unwrapped;
                }
            }

            static bool IsSpanType(Type type)
            {
                return type.IsGenericType &&
                       (type.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>) ||
                        type.GetGenericTypeDefinition() == typeof(Span<>));
            }
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

        // Above this many values an IN list stops being sent as one placeholder per value — see
        // TryBuildJsonIn. Half of the bundled SQLite's 32,766-variable budget: the budget is per STATEMENT,
        // so the rest has to stay available for the other predicates, or for a second list. Far above the
        // ~1,000-value chunks callers conventionally use, so ordinary queries are emitted exactly as before.
        private const int MaxInListParameters = 16_000;

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

            if (items.Count > MaxInListParameters &&
                TryBuildJsonIn(column, items, normalizeGuidStringIds, out var jsonInSql))
            {
                return jsonInSql;
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
        ///     Sends an oversized IN list as ONE parameter — a JSON array expanded by <c>json_each</c>.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         SQLite caps bound variables PER STATEMENT (32,766 in the bundled build), so a list beyond
        ///         that failed at prepare time with "too many SQL variables". Splitting it into OR-ed IN
        ///         groups would not help: the cap counts every placeholder in the statement, not the ones in
        ///         a single list. One JSON parameter has no such ceiling, stays fully parameterised, and
        ///         <c>x IN (SELECT value …)</c> follows the same affinity and collation rules as
        ///         <c>x IN (?, ?, …)</c> — including under <c>NOT</c>, and including a NULL in the list.
        ///     </para>
        ///     <para>
        ///         Only for values JSON carries without reinterpretation: text and integers (which is what
        ///         bool and enum values already are by the time they get here). A date, a Guid, a decimal or
        ///         a blob is bound by sqlite-net in a format that depends on the connection's settings, which
        ///         this builder cannot see — a list of those keeps the placeholder form, large or not.
        ///     </para>
        /// </remarks>
        private bool TryBuildJsonIn(string column, List<object> items, bool normalizeGuidStringIds, out string sql)
        {
            sql = string.Empty;

            var values = new object?[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                object? value = items[i];
                if (normalizeGuidStringIds)
                {
                    value = SqliteGuidStringNormalizer.NormalizeIdLikeValue(value);
                }

                value = SqliteQueryFactory.NormalizeParam(value);
                if (value is not (null or string or sbyte or byte or short or ushort or int or uint or long))
                {
                    return false;
                }

                values[i] = value;
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(
                       buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartArray();
                foreach (var value in values)
                {
                    switch (value)
                    {
                        case null:
                            writer.WriteNullValue();
                            break;
                        case string text:
                            writer.WriteStringValue(text);
                            break;
                        default:
                            writer.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                            break;
                    }
                }

                writer.WriteEndArray();
            }

            Parameters.Add(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
            sql = $"{column} IN (SELECT value FROM json_each(?))";
            return true;
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

    // Table names are held ALREADY QUOTED, so every place that emits one — FROM, JOIN, UPDATE, DELETE and
    // the table qualifier in front of a column — quotes it the same way columns always were. Bare, a table
    // named after a keyword (Order, Group, Transaction) produced SQL that did not parse.
    private readonly string _rootTable = QuoteIdentifier(SqliteQueryFactory.GetTableName(typeof(T)));

    private readonly Dictionary<Type, string> _knownTables =
        new() { [typeof(T)] = QuoteIdentifier(SqliteQueryFactory.GetTableName(typeof(T))) };

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
    private bool _allRows;
    private int? _limit;
    private int? _offset;
    private bool _distinct;
    private string? _culture;

    private bool HasJoins => _joins.Count > 0;

    #endregion
}
