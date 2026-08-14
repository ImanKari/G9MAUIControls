using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     SELECT-focused wrapper around <see cref="SqliteQueryBuilder{T}" />.
///     Limits IntelliSense to read-query operations.
/// </summary>
/// <remarks>
///     Public methods intentionally forward to <see cref="SqliteQueryBuilder{T}" /> members.
///     Refer to <see cref="SqliteQueryBuilder{T}" /> for behavior details.
/// </remarks>
public sealed class SqliteSelectQueryBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    private readonly SqliteQueryBuilder<T> _sqliteQueryBuilder;

    public SqliteSelectQueryBuilder(SqliteQueryBuilder<T> sqliteQueryBuilder)
    {
        ArgumentNullException.ThrowIfNull(sqliteQueryBuilder);
        _sqliteQueryBuilder = sqliteQueryBuilder;
    }

    #region Culture / Localization

    /// <summary>Sets the culture key, or uses current app culture when null/empty.</summary>
    public SqliteSelectQueryBuilder<T> WithCulture(string? cultureKey = null)
    {
        _sqliteQueryBuilder.WithCulture(cultureKey);
        return this;
    }

    /// <summary>Marks a root-table column as localized for this query.</summary>
    public SqliteSelectQueryBuilder<T> Localize(Expression<Func<T, object>> column)
    {
        _sqliteQueryBuilder.Localize(column);
        return this;
    }

    /// <summary>Marks a joined-table column as localized for this query.</summary>
    public SqliteSelectQueryBuilder<T> Localize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> column) where TEntity : class
    {
        _sqliteQueryBuilder.Localize(column);
        return this;
    }

    #endregion

    #region SELECT

    /// <summary>Selects all columns from the root table.</summary>
    public SqliteSelectQueryBuilder<T> SelectAll()
    {
        _sqliteQueryBuilder.SelectAll();
        return this;
    }

    /// <summary>Selects columns from the root table.</summary>
    public SqliteSelectQueryBuilder<T> Select(Expression<Func<T, object>> selector)
    {
        _sqliteQueryBuilder.Select(selector);
        return this;
    }

    /// <summary>Selects columns from a joined table.</summary>
    public SqliteSelectQueryBuilder<T> Select<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector) where TEntity : class
    {
        _sqliteQueryBuilder.Select(selector);
        return this;
    }

    /// <summary>Selects <c>COUNT(*)</c>.</summary>
    public SqliteSelectQueryBuilder<T> SelectCount()
    {
        _sqliteQueryBuilder.SelectCount();
        return this;
    }

    /// <summary>Adds a raw SQL expression to the SELECT list.</summary>
    public SqliteSelectQueryBuilder<T> SelectRaw(string raw)
    {
        _sqliteQueryBuilder.SelectRaw(raw);
        return this;
    }

    /// <summary>Adds a parameterized raw SQL expression to the SELECT list.</summary>
    public SqliteSelectQueryBuilder<T> SelectRaw(string raw, params object[] args)
    {
        _sqliteQueryBuilder.SelectRaw(raw, args);
        return this;
    }

    /// <summary>Selects a constant value with an alias.</summary>
    public SqliteSelectQueryBuilder<T> SelectValue(object? value, string alias)
    {
        _sqliteQueryBuilder.SelectValue(value, alias);
        return this;
    }

    /// <summary>Selects <c>COALESCE(column, fallbackValue)</c> with an alias.</summary>
    public SqliteSelectQueryBuilder<T> SelectCoalesce<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        Expression<Func<TEntity, object>> column,
        object? fallbackValue,
        string alias) where TEntity : class
    {
        _sqliteQueryBuilder.SelectCoalesce(column, fallbackValue, alias);
        return this;
    }

    /// <summary>Selects <c>COALESCE(primaryColumn, fallbackColumn)</c> with an alias.</summary>
    public SqliteSelectQueryBuilder<T> SelectCoalesce<TPrimaryEntity, TFallbackEntity>(
        Expression<Func<TPrimaryEntity, object>> primaryColumn,
        Expression<Func<TFallbackEntity, object>> fallbackColumn,
        string alias)
        where TPrimaryEntity : class
        where TFallbackEntity : class
    {
        _sqliteQueryBuilder.SelectCoalesce(primaryColumn, fallbackColumn, alias);
        return this;
    }

    #endregion

    #region JOIN

    /// <summary>Adds a join from root table to another entity with explicit join type.</summary>
    public SqliteSelectQueryBuilder<T> Join<TJoin>(Expression<Func<T, TJoin, bool>> on, string joinType = "LEFT JOIN")
        where TJoin : class
    {
        _sqliteQueryBuilder.Join<TJoin>(on, joinType);
        return this;
    }

    /// <summary>Adds a join between two known query entities with explicit join type.</summary>
    public SqliteSelectQueryBuilder<T> Join<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on,
        string joinType = "LEFT JOIN")
        where TLeft : class where TRight : class
    {
        _sqliteQueryBuilder.Join(on, joinType);
        return this;
    }

    /// <summary>Adds a LEFT JOIN from root table to another entity.</summary>
    public SqliteSelectQueryBuilder<T> LeftJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
    {
        _sqliteQueryBuilder.LeftJoin<TJoin>(on);
        return this;
    }

    /// <summary>Adds an INNER JOIN from root table to another entity.</summary>
    public SqliteSelectQueryBuilder<T> InnerJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
    {
        _sqliteQueryBuilder.InnerJoin<TJoin>(on);
        return this;
    }

    /// <summary>Adds a LEFT JOIN between two known query entities.</summary>
    public SqliteSelectQueryBuilder<T> LeftJoin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on)
        where TLeft : class where TRight : class
    {
        _sqliteQueryBuilder.LeftJoin(on);
        return this;
    }

    /// <summary>Adds an INNER JOIN between two known query entities.</summary>
    public SqliteSelectQueryBuilder<T> InnerJoin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on)
        where TLeft : class where TRight : class
    {
        _sqliteQueryBuilder.InnerJoin(on);
        return this;
    }

    #endregion

    #region WHERE

    /// <summary>Adds a WHERE predicate on the root entity.</summary>
    public SqliteSelectQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        _sqliteQueryBuilder.Where(predicate);
        return this;
    }

    /// <summary>Adds a WHERE predicate on a joined entity.</summary>
    public SqliteSelectQueryBuilder<T> Where<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        _sqliteQueryBuilder.Where(predicate);
        return this;
    }

    /// <summary>Adds a localized WHERE predicate on the root entity.</summary>
    public SqliteSelectQueryBuilder<T> WhereLocalized(Expression<Func<T, bool>> predicate, string? cultureKey = null)
    {
        _sqliteQueryBuilder.WhereLocalized(predicate, cultureKey);
        return this;
    }

    /// <summary>Adds a localized WHERE predicate on a joined entity.</summary>
    public SqliteSelectQueryBuilder<T> WhereLocalized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        string? cultureKey = null) where TEntity : class
    {
        _sqliteQueryBuilder.WhereLocalized(predicate, cultureKey);
        return this;
    }

    /// <summary>Adds a raw WHERE expression with optional parameters.</summary>
    public SqliteSelectQueryBuilder<T> WhereRaw(string sql, params object[] args)
    {
        _sqliteQueryBuilder.WhereRaw(sql, args);
        return this;
    }

    #endregion

    #region ORDER BY

    /// <summary>Adds ascending ORDER BY columns from the root entity.</summary>
    public SqliteSelectQueryBuilder<T> OrderBy(Expression<Func<T, object>> selector)
    {
        _sqliteQueryBuilder.OrderBy(selector);
        return this;
    }

    /// <summary>Adds descending ORDER BY columns from the root entity.</summary>
    public SqliteSelectQueryBuilder<T> OrderByDescending(Expression<Func<T, object>> selector)
    {
        _sqliteQueryBuilder.OrderByDescending(selector);
        return this;
    }

    /// <summary>Adds ascending ORDER BY columns from a joined entity.</summary>
    public SqliteSelectQueryBuilder<T> OrderBy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector)
        where TEntity : class
    {
        _sqliteQueryBuilder.OrderBy(selector);
        return this;
    }

    /// <summary>Adds descending ORDER BY columns from a joined entity.</summary>
    public SqliteSelectQueryBuilder<T> OrderByDescending<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector)
        where TEntity : class
    {
        _sqliteQueryBuilder.OrderByDescending(selector);
        return this;
    }

    /// <summary>Adds a raw ORDER BY expression.</summary>
    public SqliteSelectQueryBuilder<T> OrderByRaw(string raw)
    {
        _sqliteQueryBuilder.OrderByRaw(raw);
        return this;
    }

    #endregion

    #region GROUP BY / HAVING

    /// <summary>Adds GROUP BY columns from the root entity.</summary>
    public SqliteSelectQueryBuilder<T> GroupBy(Expression<Func<T, object>> selector)
    {
        _sqliteQueryBuilder.GroupBy(selector);
        return this;
    }

    /// <summary>Adds GROUP BY columns from a joined entity.</summary>
    public SqliteSelectQueryBuilder<T> GroupBy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector)
        where TEntity : class
    {
        _sqliteQueryBuilder.GroupBy(selector);
        return this;
    }

    /// <summary>Sets HAVING from an expression predicate.</summary>
    public SqliteSelectQueryBuilder<T> Having(Expression<Func<T, bool>> predicate)
    {
        _sqliteQueryBuilder.Having(predicate);
        return this;
    }

    /// <summary>Sets HAVING from raw SQL with optional parameters.</summary>
    public SqliteSelectQueryBuilder<T> HavingRaw(string sql, params object[] args)
    {
        _sqliteQueryBuilder.HavingRaw(sql, args);
        return this;
    }

    #endregion

    #region LIMIT / OFFSET / DISTINCT

    /// <summary>Sets LIMIT for this SELECT query.</summary>
    public SqliteSelectQueryBuilder<T> Limit(int count)
    {
        _sqliteQueryBuilder.Limit(count);
        return this;
    }

    /// <summary>Sets OFFSET for this SELECT query.</summary>
    public SqliteSelectQueryBuilder<T> Offset(int count)
    {
        _sqliteQueryBuilder.Offset(count);
        return this;
    }

    /// <summary>Marks this SELECT query as DISTINCT.</summary>
    public SqliteSelectQueryBuilder<T> Distinct()
    {
        _sqliteQueryBuilder.Distinct();
        return this;
    }

    #endregion

    #region Build

    /// <summary>Builds a parameterized SELECT SQL and ordered parameter array.</summary>
    public (string Sql, object[] Parameters) Build()
    {
        return _sqliteQueryBuilder.Build();
    }

    /// <summary>Builds a SELECT statement payload object.</summary>
    public SqlStatement BuildStatement()
    {
        return _sqliteQueryBuilder.BuildStatement();
    }

    /// <summary>Builds SELECT SQL with inlined values for diagnostics only.</summary>
    public string BuildInlinedSql()
    {
        return _sqliteQueryBuilder.BuildInlinedSql();
    }

    #endregion
}
