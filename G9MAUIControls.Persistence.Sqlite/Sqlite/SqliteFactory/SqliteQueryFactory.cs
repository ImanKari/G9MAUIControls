using SQLite;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     Fluent SQLite query builder. AOT-compatible — walks expression trees
///     without <c>Expression.Compile()</c>.
///     Builds parameterized SQL; execution goes through <c>SqliteRepository</c>.
/// </summary>
public static class SqliteQueryFactory
{
    private static readonly ConcurrentDictionary<Type, string> TableNameCache = new();

    /// <summary>Starts a SELECT-focused builder (cleaner API surface for read queries).</summary>
    public static SqliteSelectQueryBuilder<T> Select<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : class, new()
    {
        return new SqliteSelectQueryBuilder<T>(new SqliteQueryBuilder<T>());
    }

    /// <summary>Starts an UPDATE-focused builder (shows only update-relevant methods).</summary>
    public static SqliteUpdateQueryBuilder<T> Update<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : class, new()
    {
        return new SqliteUpdateQueryBuilder<T>(new SqliteQueryBuilder<T>());
    }

    /// <summary>Starts a DELETE-focused builder (shows only delete-relevant methods).</summary>
    public static SqliteDeleteQueryBuilder<T> Delete<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : class, new()
    {
        return new SqliteDeleteQueryBuilder<T>(new SqliteQueryBuilder<T>());
    }

    /// <summary>
    ///     Get the name of table from entity type, using [Table] attribute if present, otherwise fallback to type name.
    /// </summary>
    public static string GetTableName(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type type)
    {
        return TableNameCache.GetOrAdd(type, static t =>
        {
            var attr = t.GetCustomAttribute<TableAttribute>();
            return attr?.Name ?? t.Name;
        });
    }

    /// <summary>
    ///     Normalizes a parameter value for database operations by converting null, boolean, and enum values to standard
    ///     representations.
    /// </summary>
    /// <remarks>
    ///     Use this method to prepare parameter values for insertion into a database, ensuring that
    ///     nulls, Boolean values, and enumerations are converted to types commonly supported by database engines.
    /// </remarks>
    /// <param name="value">
    ///     The value to normalize. This can be null, a Boolean, an enumeration, or any other object.
    ///     Null is preserved as null so sqlite-net can bind it as SQL NULL.
    /// </param>
    /// <returns>
    ///     An object representing the normalized value: 1 for <see langword="true" />, 0 for <see langword="false" />, the
    ///     integer value of an enumeration, or the original value if it does not match any of these types.
    /// </returns>
    public static object NormalizeParam(object? value)
    {
        return value switch
        {
            null => null!,
            bool b => b ? 1 : 0,
            Enum e => Convert.ToInt32(e),
            _ => value
        };
    }


    /// <summary>
    ///     Inlines parameters into a SQL string for debugging purposes. Replaces each '?' placeholder in the SQL with the
    ///     corresponding
    /// </summary>
    public static string InlineSql(string sql, IReadOnlyList<object> parameters)
    {
        if (parameters.Count == 0)
        {
            return sql;
        }

        var sb = new StringBuilder(sql.Length + (parameters.Count * 12));
        var paramIndex = 0;

        foreach (var ch in sql)
        {
            if (ch == '?' && paramIndex < parameters.Count)
            {
                sb.Append(ToSqlLiteral(parameters[paramIndex++]));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Converts a value to its SQL literal representation for debugging purposes. Handles various types including strings,
    ///     numbers, booleans,
    /// </summary>
    public static string ToSqlLiteral(object? value)
    {
        return value switch
        {
            null or DBNull => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            char c => $"'{c.ToString().Replace("'", "''")}'",
            bool boolValue => boolValue ? "1" : "0",
            byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
            DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture)}'",
            DateTimeOffset dto => $"'{dto.ToString("O", CultureInfo.InvariantCulture)}'",
            Guid g => $"'{g:D}'",
            Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            sbyte sbyteValue => sbyteValue.ToString(CultureInfo.InvariantCulture),
            byte byteValue => byteValue.ToString(CultureInfo.InvariantCulture),
            short s => s.ToString(CultureInfo.InvariantCulture),
            ushort us => us.ToString(CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            uint ui => ui.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            ulong ul => ul.ToString(CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) is { Length: > 0 } s
                ? s
                : "NULL",
            _ => $"'{value.ToString()?.Replace("'", "''") ?? string.Empty}'"
        };
    }
}