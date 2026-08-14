namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Marks a string property as a GUID-backed SQLite ID column.
///     The SQLite wrapper normalizes marked values to uppercase GUID D format for writes and typed query parameters.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SqliteGuidIdColumnAttribute : Attribute;
