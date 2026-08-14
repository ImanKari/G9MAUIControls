using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

public sealed partial class SqliteQueryBuilder<T> where T : class, new()
{
    #region Set

    /// <summary>Adds a SET clause for UPDATE: <c>.Set(x => x.Status, 3)</c>.</summary>
    public SqliteQueryBuilder<T> Set<TValue>(Expression<Func<T, TValue>> column, TValue value)
    {
        _setClauses.Add(new SetClause(ExtractMemberName(column), value));
        return this;
    }

    #endregion

    #region Culture and Localization

    /// <summary>Sets the culture key, or uses current app culture when null/empty.</summary>
    public SqliteQueryBuilder<T> WithCulture(string? cultureKey = null)
    {
        if (string.IsNullOrWhiteSpace(cultureKey))
        {
            // The UI suite's culture facade is a CORE type and this package deliberately does not
            // reference the core. CurrentUICulture is the framework answer and is what a MAUI app's
            // localization sets anyway.
            var iso = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            _culture = char.ToUpperInvariant(iso[0]) + iso[1..];
            return this;
        }

        _culture = cultureKey;
        return this;
    }

    /// <summary>Explicitly marks a column for json_extract in this query.</summary>
    public SqliteQueryBuilder<T> Localize(Expression<Func<T, object>> column)
    {
        MarkLocalized(typeof(T), ExtractMemberName(column));
        return this;
    }

    /// <summary>Explicitly marks a column from a joined table for json_extract in this query.</summary>
    public SqliteQueryBuilder<T> Localize<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity>(Expression<Func<TEntity, object>> column) where TEntity : class
    {
        EnsureTableKnown(typeof(TEntity));
        MarkLocalized(typeof(TEntity), ExtractMemberName(column));
        return this;
    }

    private void MarkLocalized(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType,
        string propertyName)
    {
        _ = ResolveColumnName(propertyName, entityType);
        _localizedOverrides.Add((entityType, propertyName));
    }

    private void MarkLocalizedMembers(
        Expression body,
        ParameterExpression parameter,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        foreach (var memberName in ExtractMemberNames(body, parameter))
        {
            MarkLocalized(entityType, memberName);
        }
    }

    #endregion

    #region Limit Offset Distinct

    /// <summary>Adds LIMIT to the query.</summary>
    public SqliteQueryBuilder<T> Limit(int count)
    {
        _limit = count;
        return this;
    }

    /// <summary>Adds OFFSET to the query.</summary>
    public SqliteQueryBuilder<T> Offset(int count)
    {
        _offset = count;
        return this;
    }

    /// <summary>Marks the SELECT as DISTINCT.</summary>
    public SqliteQueryBuilder<T> Distinct()
    {
        _distinct = true;
        return this;
    }

    #endregion
}