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
    private static G9IdCase _canonicalCase = G9IdCase.Lower;
    private static bool _canonicalCaseObserved;
    private static readonly Lock CanonicalCaseGate = new();

    /// <summary>
    ///     Applies <see cref="G9SqliteOptions.CanonicalIdCase" /> to this normaliser. Called once by
    ///     <c>AddG9Sqlite</c> while the options are being frozen.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why a static, and why it is write-once.</b> Normalisation is reached through static
    ///         extension methods from hundreds of call sites, none of which can see the options object, so
    ///         the setting has nowhere else to live. It is frozen the first time an id is normalised because
    ///         changing the canonical case mid-session is not a setting change, it is a data event: ids
    ///         already written keep the old casing, and any path that derives a FILE NAME from a normalised
    ///         id (a per-user database directory, for instance) would start pointing somewhere else — which
    ///         presents to the user as total data loss, not as a formatting change.
    ///     </para>
    ///     <para>
    ///         Until this was wired, <see cref="G9SqliteOptions.CanonicalIdCase" /> was accepted from the
    ///         builder and then ignored: the normaliser was hard-coded to upper case. A consumer calling
    ///         <c>UseCanonicalIdCase</c> got no error and no effect.
    ///     </para>
    ///     <para>
    ///         <b>The default is <c>Lower</c>, and upper case was a mistake.</b> When the setting was first
    ///         wired up the default was set to <c>Upper</c> to match the library's existing hard-coded
    ///         behaviour. That preserved bug-compatibility at the cost of correctness: every other layer in
    ///         the stack — <c>Guid.ToString("D")</c>, RFC 4122, PostgreSQL <c>uuid</c>, Dotmim.Sync's wire
    ///         format — emits LOWER case, and a sync engine that writes rows straight into SQLite bypasses
    ///         this normaliser entirely. Upper therefore guaranteed that a normalised id and a
    ///         server-written id were different STRINGS for the same entity. In SQL that was invisible
    ///         (id columns are <c>COLLATE NOCASE</c>), but every ORDINAL comparison — a dictionary key, a
    ///         <c>HashSet</c>, a hashed sync-scope parameter, a derived file path — silently failed to
    ///         match. See <c>G9SqliteOptions.CanonicalIdCase</c> for the measured evidence.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     A different case is requested after ids have already been normalised in this process.
    /// </exception>
    public static void UseCanonicalCase(G9IdCase idCase)
    {
        lock (CanonicalCaseGate)
        {
            if (_canonicalCase == idCase)
            {
                return;
            }

            if (_canonicalCaseObserved)
            {
                throw new InvalidOperationException(
                    $"The canonical GUID case is already fixed to '{_canonicalCase}' and ids have been " +
                    $"normalised with it; changing it to '{idCase}' now would silently stop matching every " +
                    "row already written, and orphan anything keyed by a normalised id (per-user database " +
                    "directories, preference keys). Configure it once, before the first database touch.");
            }

            _canonicalCase = idCase;
        }
    }

    public static string CreateNewId()
    {
        return ToCanonicalString(Guid.NewGuid());
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
        // Reading the setting is what freezes it — see UseCanonicalCase.
        _canonicalCaseObserved = true;
        var text = guid.ToString("D");
        return _canonicalCase == G9IdCase.Lower ? text.ToLowerInvariant() : text.ToUpperInvariant();
    }
}
