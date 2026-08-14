using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     DELETE-focused wrapper around <see cref="SqliteQueryBuilder{T}" />.
///     Limits IntelliSense to delete-relevant operations.
/// </summary>
/// <remarks>
///     Public methods intentionally forward to <see cref="SqliteQueryBuilder{T}" /> members.
///     Refer to <see cref="SqliteQueryBuilder{T}" /> for behavior details.
/// </remarks>
public sealed class SqliteDeleteQueryBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    private readonly SqliteQueryBuilder<T> _sqliteQueryBuilder;

    public SqliteDeleteQueryBuilder(SqliteQueryBuilder<T> sqliteQueryBuilder)
    {
        ArgumentNullException.ThrowIfNull(sqliteQueryBuilder);
        _sqliteQueryBuilder = sqliteQueryBuilder;
    }

    #region Culture / Localization

    /// <summary>Sets the culture key, or uses current app culture when null/empty.</summary>
    public SqliteDeleteQueryBuilder<T> WithCulture(string? cultureKey = null)
    {
        _sqliteQueryBuilder.WithCulture(cultureKey);
        return this;
    }

    /// <summary>Marks a root-table column as localized for this query.</summary>
    public SqliteDeleteQueryBuilder<T> Localize(Expression<Func<T, object>> column)
    {
        _sqliteQueryBuilder.Localize(column);
        return this;
    }

    /// <summary>Marks a joined-table column as localized for this query.</summary>
    public SqliteDeleteQueryBuilder<T> Localize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> column) where TEntity : class
    {
        _sqliteQueryBuilder.Localize(column);
        return this;
    }

    #endregion

    #region WHERE

    /// <summary>Adds a WHERE predicate on the root entity.</summary>
    public SqliteDeleteQueryBuilder<T> Where(Expression<Func<T, bool>> predicate)
    {
        _sqliteQueryBuilder.Where(predicate);
        return this;
    }

    /// <summary>Adds a WHERE predicate on a joined entity.</summary>
    public SqliteDeleteQueryBuilder<T> Where<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, bool>> predicate) where TEntity : class
    {
        _sqliteQueryBuilder.Where(predicate);
        return this;
    }

    /// <summary>Adds a localized WHERE predicate on the root entity.</summary>
    public SqliteDeleteQueryBuilder<T> WhereLocalized(Expression<Func<T, bool>> predicate, string? cultureKey = null)
    {
        _sqliteQueryBuilder.WhereLocalized(predicate, cultureKey);
        return this;
    }

    /// <summary>Adds a localized WHERE predicate on a joined entity.</summary>
    public SqliteDeleteQueryBuilder<T> WhereLocalized<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        string? cultureKey = null) where TEntity : class
    {
        _sqliteQueryBuilder.WhereLocalized(predicate, cultureKey);
        return this;
    }

    /// <summary>Adds a raw WHERE expression with optional parameters.</summary>
    public SqliteDeleteQueryBuilder<T> WhereRaw(string sql, params object[] args)
    {
        _sqliteQueryBuilder.WhereRaw(sql, args);
        return this;
    }

    #endregion

    #region Build

    /// <summary>Builds a parameterized DELETE SQL and ordered parameter array.</summary>
    public (string Sql, object[] Parameters) Build()
    {
        return _sqliteQueryBuilder.BuildDelete();
    }

    /// <summary>Builds a DELETE statement payload object.</summary>
    public SqlStatement BuildStatement()
    {
        return _sqliteQueryBuilder.BuildDeleteStatement();
    }

    /// <summary>Builds DELETE SQL with inlined values for diagnostics only.</summary>
    public string BuildInlinedSql()
    {
        return _sqliteQueryBuilder.BuildDeleteInlinedSql();
    }

    #endregion
}
