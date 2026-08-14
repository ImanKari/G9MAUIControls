using System.Reflection;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     The canonical form of an id in this persistence layer, and the extension methods that produce it.
/// </summary>
/// <remarks>
///     <b>Public, and NOT in an Internal namespace, because it is part of the storage contract.</b> Ids are
///     stored as GUID strings, and SQLite compares strings by ORDINAL bytes — so a row written with one
///     casing or bracket style is simply not found by a lookup that uses another. Every consumer that
///     builds an id, or compares one, has to use the same normalisation the repository uses, which means
///     it cannot be an implementation detail. It briefly was one, and 200+ call sites in the first real
///     consumer could not reach it.
/// </remarks>
public static class SqliteGuidStringNormalizer
{
    public static string CreateNewId()
    {
        return Guid.NewGuid().ToString("D").ToUpperInvariant();
    }

    public static string? NormalizeOptionalId(string? value)
    {
        return value.NormalizeSqliteGuidId();
    }

    public static string NormalizeRequiredId(string? value)
    {
        return value.NormalizeRequiredSqliteGuidId();
    }

    public static string? NormalizeSqliteGuidId(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return Guid.TryParse(trimmed, out var guid)
            ? ToCanonicalString(guid)
            : trimmed;
    }

    /// <summary>
    ///     Guid overloads of <see cref="NormalizeSqliteGuidId(string?)" />. The server DTOs expose ids
    ///     as <see cref="Guid" /> (<c>BaseDto.Id</c>), while the client's SQLite mirror stores every id
    ///     as its canonical STRING form — these are the single conversion seam between the two, so no
    ///     call site has to hand-roll a <c>ToString("D").ToUpperInvariant()</c> and risk a
    ///     casing/format mismatch that would silently break id comparisons.
    /// </summary>
    public static string NormalizeSqliteGuidId(this Guid value)
    {
        return ToCanonicalString(value);
    }

    /// <inheritdoc cref="NormalizeSqliteGuidId(Guid)" />
    public static string? NormalizeSqliteGuidId(this Guid? value)
    {
        return value is { } guid ? ToCanonicalString(guid) : null;
    }

    public static string NormalizeRequiredSqliteGuidId(this string? value)
    {
        return value.NormalizeSqliteGuidId() ?? string.Empty;
    }

    public static object? NormalizeIdLikeValue(object? value)
    {
        return value switch
        {
            null => null,
            Guid guid => ToCanonicalString(guid),
            string text => NormalizeRequiredId(text),
            _ => value
        };
    }

    public static bool IsGuidStringIdProperty(PropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (property.PropertyType != typeof(string))
        {
            return false;
        }

        return string.Equals(property.Name, "Id", StringComparison.Ordinal) ||
               property.GetCustomAttribute<SqliteGuidIdColumnAttribute>() != null;
    }

    public static bool IsGuidStringIdProperty(string propertyName, Type propertyType)
    {
        return propertyType == typeof(string) &&
               string.Equals(propertyName, "Id", StringComparison.Ordinal);
    }

    private static string ToCanonicalString(Guid guid)
    {
        return guid.ToString("D").ToUpperInvariant();
    }
}
