using G9MAUIControls.Persistence.Sqlite.Queries;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Marks a property as containing multilingual JSON (e.g. {"En":"...","Fa":"..."}).
///     When a culture is set via <see cref="SqliteQueryBuilder{T}.WithCulture" />,
///     SELECT/WHERE will use <c>json_extract(column, '$.{culture}')</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SqliteLocalizedColumnAttribute : Attribute;
