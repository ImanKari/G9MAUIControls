namespace G9MAUIControls.Persistence.Sqlite.Queries;

/// <summary>
///     Built SQL command payload. Use <see cref="Sql" /> + <see cref="Parameters" /> for execution.
///     <see cref="InlinedSql" /> is for diagnostics only.
/// </summary>
public readonly record struct SqlStatement(string Sql, object[] Parameters)
{
    /// <summary>
    ///     SQL text with parameters rendered as literals.
    ///     Intended for diagnostics and logging only.
    /// </summary>
    public string InlinedSql => SqliteQueryFactory.InlineSql(Sql, Parameters);
}