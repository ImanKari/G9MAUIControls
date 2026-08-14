using G9MAUIControls.Persistence.Sqlite;
using SQLite;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

public sealed partial class SqliteQueryBuilder<T> where T : class, new()
{
    private SqlVisitor CreateVisitor(
        Dictionary<ParameterExpression, (string Table, Type Type)> paramMap,
        bool forceQualifiedColumns = false)
    {
        return new SqlVisitor(
            paramMap,
            _culture,
            IsLocalized,
            ResolveColumnName,
            IsGuidStringIdColumn,
            forceQualifiedColumns || HasJoins);
    }

    private static HashSet<string> ExtractMemberNames(Expression body, ParameterExpression parameter)
    {
        var collector = new ParameterMemberCollector(parameter);
        collector.Visit(body);
        return collector.MemberNames;
    }

    private bool IsLocalized(
        string propertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        if (_localizedOverrides.Contains((entityType, propertyName)))
        {
            return true;
        }

        return LocalizedColumnCache.GetOrAdd((entityType, propertyName), static key =>
        {
            var map = GetEntityColumnMap(key.EntityType);
            return map.PropertyMaps.TryGetValue(key.PropertyName, out var propertyMap)
                   && propertyMap is { IsIgnored: false, IsLocalized: true };
        });
    }

    private static string[] GetSelectablePropertyNames(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        return GetEntityColumnMap(entityType).SelectablePropertyNames;
    }

    private string ResolveColumnName(
        string propertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var map = GetEntityColumnMap(entityType);
        if (!map.PropertyMaps.TryGetValue(propertyName, out var propertyMap))
        {
            throw new InvalidOperationException(
                $"Property '{entityType.Name}.{propertyName}' is not a readable mapped column.");
        }

        if (propertyMap.IsIgnored)
        {
            throw new InvalidOperationException(
                $"Property '{entityType.Name}.{propertyName}' is marked with [Ignore] and cannot be used in SQL expressions.");
        }

        return propertyMap.ColumnName;
    }

    private static EntityColumnMap GetEntityColumnMap(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        return EntityColumnMapCache.GetOrAdd(entityType, static type =>
        {
            var propertyMaps = new Dictionary<string, EntityPropertyMap>(StringComparer.Ordinal);
            var selectablePropertyNames = new List<string>();

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead))
            {
                var isIgnored = prop.GetCustomAttribute<IgnoreAttribute>() != null;
                var columnName = prop.GetCustomAttribute<ColumnAttribute>()?.Name ?? prop.Name;
                var isLocalized = !isIgnored && prop.GetCustomAttribute<SqliteLocalizedColumnAttribute>() != null;
                var isGuidStringId = !isIgnored && SqliteGuidStringNormalizer.IsGuidStringIdProperty(prop);

                propertyMaps[prop.Name] = new EntityPropertyMap(columnName, isLocalized, isIgnored, isGuidStringId);

                if (!isIgnored)
                {
                    selectablePropertyNames.Add(prop.Name);
                }
            }

            return new EntityColumnMap
            {
                SelectablePropertyNames = selectablePropertyNames.ToArray(), PropertyMaps = propertyMaps
            };
        });
    }

    private string FormatColumn(
        string tableName,
        string propName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var col = FormatColumnExpression(tableName, propName, entityType);
        if (_culture != null && IsLocalized(propName, entityType))
        {
            return $"{col} AS {QuoteIdentifier(propName)}";
        }

        return col;
    }

    private static bool IsGuidStringIdColumn(
        string propertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var map = GetEntityColumnMap(entityType);
        return map.PropertyMaps.TryGetValue(propertyName, out var propertyMap) &&
               propertyMap is { IsIgnored: false, IsGuidStringId: true };
    }

    private static object NormalizeParamForProperty(
        string propertyName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType,
        object? value)
    {
        if (IsGuidStringIdColumn(propertyName, entityType))
        {
            value = SqliteGuidStringNormalizer.NormalizeIdLikeValue(value);
        }

        return SqliteQueryFactory.NormalizeParam(value);
    }

    private string FormatColumnExpression(
        string tableName,
        string propName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var columnName = ResolveColumnName(propName, entityType);
        var col = HasJoins ? $"{tableName}.{QuoteIdentifier(columnName)}" : QuoteIdentifier(columnName);
        return _culture != null && IsLocalized(propName, entityType)
            ? BuildLocalizedValueExpression(col, _culture)
            : col;
    }

    private static string BuildLocalizedValueExpression(string columnSql, string cultureKey)
    {
        var escapedCulture = cultureKey.Replace("'", "''");
        return
            $"CASE WHEN json_valid({columnSql}) = 1 THEN COALESCE(json_extract({columnSql}, '$.{escapedCulture}'), '') ELSE {columnSql} END";
    }

    private string ResolveColumnExpression<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector) where TEntity : class
    {
        var entityType = typeof(TEntity);
        EnsureTableKnown(entityType);
        var tableName = _knownTables[entityType];
        var memberName = ExtractMemberName(selector);
        return FormatColumnExpression(tableName, memberName, entityType);
    }

    private void AddSelectPart(string sql)
    {
        _selectParts.Add(sql);
    }

    private void AddSelectPart(string sql, params object?[] args)
    {
        _selectParts.Add(sql);
        AddNormalizedParams(_selectParams, args);
    }

    private static void AddNormalizedParams(List<object> target, IEnumerable<object?> args)
    {
        foreach (var arg in args)
        {
            target.Add(SqliteQueryFactory.NormalizeParam(arg));
        }
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private void AddSelectFromExpression(
        Expression body,
        ParameterExpression param,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var tableName = _knownTables[entityType];

        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        switch (body)
        {
            case NewExpression newExpr:
                for (var i = 0; i < newExpr.Arguments.Count; i++)
                {
                    var alias = newExpr.Members![i].Name;
                    var argBody = newExpr.Arguments[i];

                    if (argBody is UnaryExpression { NodeType: ExpressionType.Convert } u)
                    {
                        argBody = u.Operand;
                    }

                    if (argBody is MemberExpression mem && mem.Expression == param)
                    {
                        AddSelectPart(FormatAliasedColumn(tableName, mem.Member.Name, alias, entityType));
                    }
                    else
                    {
                        var paramMap = new Dictionary<ParameterExpression, (string, Type)>
                        {
                            [param] = (tableName, entityType)
                        };

                        var visitor = CreateVisitor(paramMap);
                        var sql = visitor.Visit(argBody);
                        AddSelectPart($"{sql} AS [{alias}]", visitor.Parameters.ToArray());
                    }
                }

                break;

            case MemberInitExpression initExpr:
                foreach (var binding in initExpr.Bindings.OfType<MemberAssignment>())
                {
                    var alias = binding.Member.Name;
                    var valExpr = binding.Expression;
                    if (valExpr is UnaryExpression { NodeType: ExpressionType.Convert } u2)
                    {
                        valExpr = u2.Operand;
                    }

                    if (valExpr is MemberExpression m2 && m2.Expression == param)
                    {
                        AddSelectPart(FormatAliasedColumn(tableName, m2.Member.Name, alias, entityType));
                    }
                    else
                    {
                        var paramMap = new Dictionary<ParameterExpression, (string, Type)>
                        {
                            [param] = (tableName, entityType)
                        };

                        var visitor = CreateVisitor(paramMap);
                        AddSelectPart($"{visitor.Visit(valExpr)} AS [{alias}]", visitor.Parameters.ToArray());
                    }
                }

                break;

            case MemberExpression single when single.Expression == param:
                AddSelectPart(FormatColumn(tableName, single.Member.Name, entityType));
                break;

            default:
                throw new NotSupportedException(
                    $"Unsupported Select expression type: {body.NodeType}. Use anonymous type or single property.");
        }
    }

    private string FormatAliasedColumn(
        string tableName,
        string propertyName,
        string alias,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        return string.Equals(propertyName, alias, StringComparison.Ordinal)
            ? FormatColumn(tableName, propertyName, entityType)
            : $"{FormatColumnExpression(tableName, propertyName, entityType)} AS {QuoteIdentifier(alias)}";
    }

    private void AddOrderByColumns(
        Expression body,
        ParameterExpression param,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType,
        bool descending)
    {
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        var tableName = _knownTables[entityType];
        var suffix = descending ? " DESC" : " ASC";

        switch (body)
        {
            case MemberExpression m when m.Expression == param:
                _orderByParts.Add(ColRef(tableName, m.Member.Name, entityType) + suffix);
                break;
            case NewExpression n:
                foreach (var arg in n.Arguments)
                {
                    var a = arg is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : arg;
                    if (a is MemberExpression mem && mem.Expression == param)
                    {
                        _orderByParts.Add(ColRef(tableName, mem.Member.Name, entityType) + suffix);
                    }
                }

                break;
            default:
                throw new NotSupportedException("OrderBy expression must be a property or anonymous type.");
        }
    }

    private void AddGroupByColumns(
        Expression body,
        ParameterExpression param,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        var tableName = _knownTables[entityType];

        switch (body)
        {
            case MemberExpression m when m.Expression == param:
                _groupByParts.Add(ColRef(tableName, m.Member.Name, entityType));
                break;
            case NewExpression n:
                foreach (var arg in n.Arguments)
                {
                    var a = arg is UnaryExpression { NodeType: ExpressionType.Convert } uc ? uc.Operand : arg;
                    if (a is MemberExpression mem && mem.Expression == param)
                    {
                        _groupByParts.Add(ColRef(tableName, mem.Member.Name, entityType));
                    }
                }

                break;
            default:
                throw new NotSupportedException("GroupBy expression must be a property or anonymous type.");
        }
    }

    private SqliteQueryBuilder<T> AddJoin(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type leftType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type rightType,
        IReadOnlyList<ParameterExpression> parameters,
        Expression body,
        string joinType)
    {
        EnsureTableKnown(leftType);
        var rightTable = SqliteQueryFactory.GetTableName(rightType);
        _knownTables.TryAdd(rightType, rightTable);

        var leftTable = _knownTables[leftType];
        var paramMap = new Dictionary<ParameterExpression, (string Table, Type Type)>
        {
            [parameters[0]] = (leftTable, leftType), [parameters[1]] = (rightTable, rightType)
        };

        var visitor = CreateVisitor(paramMap, true);
        var onSql = visitor.Visit(body);
        _joins.Add(new JoinClause(joinType, rightTable, onSql, visitor.Parameters.ToArray()));
        return this;
    }

    private string ColRef(
        string tableName,
        string propName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        var columnName = ResolveColumnName(propName, entityType);
        return HasJoins ? $"{tableName}.{QuoteIdentifier(columnName)}" : QuoteIdentifier(columnName);
    }

    private void EnsureTableKnown([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
    {
        if (!_knownTables.ContainsKey(type))
        {
            throw new InvalidOperationException(
                $"Type {type.Name} is not registered. Start from Select/Update/Delete or add Join first.");
        }
    }

    private static string ExtractMemberName<TEntity, TValue>(Expression<Func<TEntity, TValue>> selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } u)
        {
            body = u.Operand;
        }

        if (body is MemberExpression m)
        {
            return m.Member.Name;
        }

        throw new ArgumentException("Expression must be a simple property accessor.");
    }

    private sealed class ParameterMemberCollector(ParameterExpression parameter) : ExpressionVisitor
    {
        public HashSet<string> MemberNames { get; } = [];

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression == parameter)
            {
                MemberNames.Add(node.Member.Name);
            }

            return base.VisitMember(node);
        }
    }
}
