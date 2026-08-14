using Microsoft.Extensions.Logging;
using SQLite;

namespace G9MAUIControls.Persistence.Sqlite.Migrations;

/// <summary>
///     Defines a contract for executing a single database migration step using an asynchronous SQLite connection.
/// </summary>
/// <remarks>
///     Implementations of this interface should provide the logic for migrating the database schema or data.
///     The migration process is expected to be asynchronous, allowing for non-blocking database operations.
///     Migrations MUST be idempotent (safe to re-run) and MUST guard against missing tables — see
///     <c>AiGuides/05-Client-Migrations.md</c>.
/// </remarks>
public interface ISqliteMigration
{
    Task MigrateAsync(SQLiteAsyncConnection db, ILogger logger);
}
