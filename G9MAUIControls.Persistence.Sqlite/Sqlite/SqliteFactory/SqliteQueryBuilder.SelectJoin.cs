using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

public sealed partial class SqliteQueryBuilder<T> where T : class, new()
{
    #region Select

    /// <summary>SELECT * (or all columns with json_extract when culture is set).</summary>
    public SqliteQueryBuilder<T> SelectAll()
    {
        if (_culture == null)
        {
            _selectParts.Add(HasJoins ? $"{_rootTable}.*" : "*");
            return this;
        }

        foreach (var propertyName in GetSelectablePropertyNames(typeof(T)))
        {
            _selectParts.Add(FormatColumn(_rootTable, propertyName, typeof(T)));
        }

        return this;
    }

    /// <summary>SELECT specific columns: <c>.Select(x => new { x.Id, x.Title })</c> or <c>.Select(x => x.Id)</c>.</summary>
    public SqliteQueryBuilder<T> Select(Expression<Func<T, object>> selector)
    {
        AddSelectFromExpression(selector.Body, selector.Parameters[0], typeof(T));
        return this;
    }

    /// <summary>SELECT from a joined table: <c>.Select&lt;OtherEntity&gt;(o => new { o.Code })</c>.</summary>
    public SqliteQueryBuilder<T> Select<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> selector) where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        AddSelectFromExpression(selector.Body, selector.Parameters[0], typeof(TEntity));
        return this;
    }

    /// <summary>SELECT COUNT(*) convenience.</summary>
    public SqliteQueryBuilder<T> SelectCount()
    {
        _selectParts.Clear();
        _selectParams.Clear();
        _selectParts.Add("COUNT(*)");
        return this;
    }

    /// <summary>Appends a raw SQL fragment to the SELECT list.</summary>
    public SqliteQueryBuilder<T> SelectRaw(string raw)
    {
        AddSelectPart(raw);
        return this;
    }

    /// <summary>Appends a raw SQL fragment with optional parameters to the SELECT list.</summary>
    public SqliteQueryBuilder<T> SelectRaw(string raw, params object[] args)
    {
        AddSelectPart(raw, args);
        return this;
    }

    /// <summary>Selects a constant value with alias: <c>.SelectValue(1, "Synced")</c>.</summary>
    public SqliteQueryBuilder<T> SelectValue(object? value, string alias)
    {
        AddSelectPart($"? AS {QuoteIdentifier(alias)}", value);
        return this;
    }

    /// <summary>
    ///     Selects <c>COALESCE(column, fallbackValue)</c> as <paramref name="alias" />.
    ///     Useful for LEFT JOIN projections where the joined row may be null.
    /// </summary>
    public SqliteQueryBuilder<T> SelectCoalesce<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(
        Expression<Func<TEntity, object>> column,
        object? fallbackValue,
        string alias) where TEntity : class
    {
        var primaryColumn = ResolveColumnExpression(column);
        AddSelectPart($"COALESCE({primaryColumn}, ?) AS {QuoteIdentifier(alias)}", fallbackValue);
        return this;
    }

    /// <summary>
    ///     Selects <c>COALESCE(primaryColumn, fallbackColumn)</c> as <paramref name="alias" />.
    /// </summary>
    public SqliteQueryBuilder<T> SelectCoalesce<TPrimaryEntity, TFallbackEntity>(
        Expression<Func<TPrimaryEntity, object>> primaryColumn,
        Expression<Func<TFallbackEntity, object>> fallbackColumn,
        string alias)
        where TPrimaryEntity : class
        where TFallbackEntity : class
    {
        var primary = ResolveColumnExpression(primaryColumn);
        var fallback = ResolveColumnExpression(fallbackColumn);
        AddSelectPart($"COALESCE({primary}, {fallback}) AS {QuoteIdentifier(alias)}");
        return this;
    }

    #endregion

    #region Join

    /// <summary>Adds a JOIN between the root table and <typeparamref name="TJoin" />.</summary>
    public SqliteQueryBuilder<T> Join<TJoin>(Expression<Func<T, TJoin, bool>> on, string joinType = "LEFT JOIN")
        where TJoin : class
    {
        return AddJoin(typeof(T), typeof(TJoin), on.Parameters, on.Body, joinType);
    }

    /// <summary>Adds a JOIN between any two known joined types.</summary>
    public SqliteQueryBuilder<T> Join<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on,
        string joinType = "LEFT JOIN")
        where TLeft : class where TRight : class
    {
        return AddJoin(typeof(TLeft), typeof(TRight), on.Parameters, on.Body, joinType);
    }

    /// <summary>Adds a LEFT JOIN between the root table and <typeparamref name="TJoin" />.</summary>
    public SqliteQueryBuilder<T> LeftJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
    {
        return AddJoin(typeof(T), typeof(TJoin), on.Parameters, on.Body, "LEFT JOIN");
    }

    /// <summary>Adds an INNER JOIN between the root table and <typeparamref name="TJoin" />.</summary>
    public SqliteQueryBuilder<T> InnerJoin<TJoin>(Expression<Func<T, TJoin, bool>> on) where TJoin : class
    {
        return AddJoin(typeof(T), typeof(TJoin), on.Parameters, on.Body, "INNER JOIN");
    }

    /// <summary>Adds a LEFT JOIN between two known joined types.</summary>
    public SqliteQueryBuilder<T> LeftJoin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on)
        where TLeft : class where TRight : class
    {
        return AddJoin(typeof(TLeft), typeof(TRight), on.Parameters, on.Body, "LEFT JOIN");
    }

    /// <summary>Adds an INNER JOIN between two known joined types.</summary>
    public SqliteQueryBuilder<T> InnerJoin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TLeft, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRight>(Expression<Func<TLeft, TRight, bool>> on)
        where TLeft : class where TRight : class
    {
        return AddJoin(typeof(TLeft), typeof(TRight), on.Parameters, on.Body, "INNER JOIN");
    }

    #endregion
}