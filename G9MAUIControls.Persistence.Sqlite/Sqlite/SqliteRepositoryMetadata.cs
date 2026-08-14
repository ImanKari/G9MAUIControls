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

        return new SqliteTableMetadata<T>(tableName, properties, primaryKeyProperties, lookup);
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
