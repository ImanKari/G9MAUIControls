using G9MAUIControls.Persistence.Sqlite.Queries;
using SQLite;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace G9MAUIControls.Persistence.Sqlite;

internal static class SqliteRepositoryMetadata<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : class, new()
{
    public static readonly SqliteTableMetadata<T> Value = Create();

    private static SqliteTableMetadata<T> Create()
    {
        var tableName = SqliteQueryFactory.GetTableName(typeof(T));
        var properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetCustomAttribute<IgnoreAttribute>() == null)
            .Select(p =>
            {
                var columnName = p.GetCustomAttribute<ColumnAttribute>()?.Name ?? p.Name;
                var isPrimaryKey = p.GetCustomAttribute<PrimaryKeyAttribute>() != null;
                var isGuidStringId = SqliteGuidStringNormalizer.IsGuidStringIdProperty(p);
                return new SqliteColumnBinding(p, p.Name, columnName, isPrimaryKey, isGuidStringId);
            })
            .ToArray();

        if (properties.Length == 0)
        {
            throw new InvalidOperationException(
                $"No mappable columns found for entity type {typeof(T).Name}.");
        }

        var primaryKeyProperties = properties
            .Where(p => p.IsPrimaryKey)
            .Select(p => p.PropertyName)
            .ToArray();

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in properties)
        {
            lookup[property.PropertyName] = property.ColumnName;
            lookup[property.ColumnName] = property.ColumnName;
        }

        GuardAgainstUnrecognisedAuditShape();

        return new SqliteTableMetadata<T>(tableName, properties, primaryKeyProperties, lookup);
    }

    /// <summary>
    ///     Fails loudly when an entity carries the complete audit shape but is not recognised as
    ///     <see cref="IG9AuditedEntity" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Why this guard exists (LES-0031).</b> Every audit behaviour in this library — minting
    ///         <c>Id</c>, stamping <c>CreatedTime</c>/<c>CreatedByUserId</c>, refreshing
    ///         <c>UpdatedTime</c>/<c>UpdatedByUserId</c> on both the entity-object and the fluent-SQL update
    ///         paths — hangs off a single test: <c>entity is IG9AuditedEntity</c>. If a consumer's base class
    ///         fails that test, none of it happens, and there is nothing to notice: no exception, no warning,
    ///         no failed build. The rows are simply written with an empty primary key and unstamped audit
    ///         columns, and the damage only surfaces later as a duplicate-key failure or as a server reject.
    ///     </para>
    ///     <para>
    ///         It has happened. A consumer declared its own <c>IG9AuditedEntity</c> in the namespace holding
    ///         its entity bases; C# binds a simple name to its enclosing namespace before any <c>using</c>, so
    ///         the base implemented the twin and the library's test stopped matching for <b>every</b> audited
    ///         entity in that app at once. A type that has all five audit members but does not implement the
    ///         interface is therefore never a legitimate design — it is this bug — so it is refused here, once
    ///         per entity type, at the first repository use.
    ///     </para>
    ///     <para>
    ///         Partial shapes are deliberately allowed: an entity with <c>Id</c> + <c>CreatedTime</c> but no
    ///         update pair is a real, common "append-only log row" and manages its own columns.
    ///     </para>
    /// </remarks>
    private static void GuardAgainstUnrecognisedAuditShape()
    {
        if (typeof(IG9AuditedEntity).IsAssignableFrom(typeof(T)) ||
            !HasWritableProperty("Id", typeof(string)) ||
            !HasWritableProperty("CreatedTime", typeof(DateTime)) ||
            !HasWritableProperty("UpdatedTime", typeof(DateTime)) ||
            !HasWritableProperty("CreatedByUserId", typeof(string)) ||
            !HasWritableProperty("UpdatedByUserId", typeof(string)))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Entity '{typeof(T).FullName}' has every audit column (Id, CreatedTime, UpdatedTime, " +
            "CreatedByUserId, UpdatedByUserId) but does not implement " +
            $"'{typeof(IG9AuditedEntity).FullName}', so this library would silently skip Id generation " +
            "and audit stamping for it — writing rows with an EMPTY primary key. The usual cause is a " +
            "type named 'IG9AuditedEntity' declared in the consumer's own namespace, which shadows the " +
            "library interface at compile time without any warning. Implement " +
            $"'{typeof(IG9AuditedEntity).FullName}' by its FULLY QUALIFIED name on the entity's base class.");
    }

    private static bool HasWritableProperty(string name, Type propertyType)
    {
        var property = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return property is { CanRead: true, CanWrite: true } &&
               (property.PropertyType == propertyType ||
                Nullable.GetUnderlyingType(property.PropertyType) == propertyType);
    }
}

internal sealed class SqliteTableMetadata<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : class, new()
{
    public SqliteTableMetadata(
        string tableName,
        SqliteColumnBinding[] columns,
        string[] primaryKeyPropertyNames,
        Dictionary<string, string> propertyOrColumnToColumn)
    {
        TableName = tableName;
        Columns = columns;
        PrimaryKeyPropertyNames = primaryKeyPropertyNames;
        PropertyOrColumnToColumn = propertyOrColumnToColumn;
    }

    public string TableName { get; }
    public SqliteColumnBinding[] Columns { get; }
    public string[] PrimaryKeyPropertyNames { get; }
    public Dictionary<string, string> PropertyOrColumnToColumn { get; }
}

internal readonly record struct SqliteColumnBinding(
    PropertyInfo Property,
    string PropertyName,
    string ColumnName,
    bool IsPrimaryKey,
    bool IsGuidStringId);
