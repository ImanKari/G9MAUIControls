using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

public sealed partial class SqliteQueryBuilder<T> where T : class, new()
{
    #region Where

    /// <summary>Adds a WHERE condition (multiple calls are ANDed).</summary>
    public SqliteQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        var paramMap = new Dictionary<ParameterExpression, (string Table, Type Type)>
        {
            [predicate.Parameters[0]] = (_rootTable, typeof(T))
        };

        var visitor = CreateVisitor(paramMap);
        var sql = visitor.Visit(predicate.Body);
        _whereParts.Add(sql);
        _whereParams.AddRange(visitor.Parameters);
        return this;
    }

    /// <summary>Typed WHERE for joined tables.</summary>
    public SqliteQueryBuilder<T> Where<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        var table = _knownTables[typeof(TEntity)];
        var paramMap = new Dictionary<ParameterExpression, (string Table, Type Type)>
        {
            [predicate.Parameters[0]] = (table, typeof(TEntity))
        };

        var visitor = CreateVisitor(paramMap);
        var sql = visitor.Visit(predicate.Body);
        _whereParts.Add(sql);
        _whereParams.AddRange(visitor.Parameters);
        return this;
    }

    /// <summary>
    ///     Adds a WHERE condition for localized columns. When <paramref name="cultureKey" /> is null/empty,
    ///     current app culture is used.
    /// </summary>
    public SqliteQueryBuilder<T> WhereLocalized(Expression<Func<T, bool>> predicate, string? cultureKey = null)
    {
        WithCulture(cultureKey);
        MarkLocalizedMembers(predicate.Body, predicate.Parameters[0], typeof(T));
        return Where(predicate);
    }

    /// <summary>
    ///     Adds a typed WHERE condition for localized columns from joined tables.
    ///     When <paramref name="cultureKey" /> is null/empty, current app culture is used.
    /// </summary>
    public SqliteQueryBuilder<T> WhereLocalized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, bool>> predicate,
        string? cultureKey = null)
        where TEntity : class
    {
        WithCulture(cultureKey);
        EnsureTableKnown(typeof(TEntity));
        MarkLocalizedMembers(predicate.Body, predicate.Parameters[0], typeof(TEntity));
        return Where(predicate);
    }

    /// <summary>Raw WHERE clause with parameters.</summary>
    public SqliteQueryBuilder<T> WhereRaw(string sql, params object[] args)
    {
        _whereParts.Add(sql);
        AddNormalizedParams(_whereParams, args);
        return this;
    }

    #endregion

    #region OrderBy

    /// <summary>Adds ascending ORDER BY columns from the root table.</summary>
    public SqliteQueryBuilder<T> OrderBy(Expression<Func<T, object>> selector)
    {
        AddOrderByColumns(selector.Body, selector.Parameters[0], typeof(T), false);
        return this;
    }

    /// <summary>Adds descending ORDER BY columns from the root table.</summary>
    public SqliteQueryBuilder<T> OrderByDescending(Expression<Func<T, object>> selector)
    {
        AddOrderByColumns(selector.Body, selector.Parameters[0], typeof(T), true);
        return this;
    }

    /// <summary>Adds ascending ORDER BY columns from a joined table.</summary>
    public SqliteQueryBuilder<T> OrderBy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector) where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        AddOrderByColumns(selector.Body, selector.Parameters[0], typeof(TEntity), false);
        return this;
    }

    /// <summary>Adds descending ORDER BY columns from a joined table.</summary>
    public SqliteQueryBuilder<T> OrderByDescending<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector)
        where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        AddOrderByColumns(selector.Body, selector.Parameters[0], typeof(TEntity), true);
        return this;
    }

    /// <summary>Raw ORDER BY: <c>.OrderByRaw("Name COLLATE NOCASE ASC")</c>.</summary>
    public SqliteQueryBuilder<T> OrderByRaw(string raw)
    {
        _orderByParts.Add(raw);
        return this;
    }

    #endregion

    #region GroupBy Having

    /// <summary>Adds GROUP BY columns from the root table.</summary>
    public SqliteQueryBuilder<T> GroupBy(Expression<Func<T, object>> selector)
    {
        AddGroupByColumns(selector.Body, selector.Parameters[0], typeof(T));
        return this;
    }

    /// <summary>Adds GROUP BY columns from a joined table.</summary>
    public SqliteQueryBuilder<T> GroupBy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector) where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        AddGroupByColumns(selector.Body, selector.Parameters[0], typeof(TEntity));
        return this;
    }

    /// <summary>Having with expression: <c>.Having(x => SqliteAggregateMaker.Count() > 1)</c>.</summary>
    public SqliteQueryBuilder<T> Having(Expression<Func<T, bool>> predicate)
    {
        var paramMap = new Dictionary<ParameterExpression, (string Table, Type Type)>
        {
            [predicate.Parameters[0]] = (_rootTable, typeof(T))
        };

        var visitor = CreateVisitor(paramMap);
        _havingSql = visitor.Visit(predicate.Body);
        _havingParams.Clear();
        _havingParams.AddRange(visitor.Parameters);
        return this;
    }

    /// <summary>Sets HAVING using raw SQL and optional parameters.</summary>
    public SqliteQueryBuilder<T> HavingRaw(string sql, params object[] args)
    {
        _havingSql = sql;
        _havingParams.Clear();
        AddNormalizedParams(_havingParams, args);
        return this;
    }

    #endregion
}
