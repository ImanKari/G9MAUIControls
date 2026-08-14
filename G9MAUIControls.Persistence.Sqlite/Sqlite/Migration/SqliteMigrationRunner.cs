using Microsoft.Extensions.Logging;
using SQLite;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite.Migrations;

/// <summary>
///     Provides functionality to register and execute SQLite database migrations in version order. Ensures that all
///     pending migrations are applied at application startup before cache initialization.
/// </summary>
/// <remarks>
///     Use this class to manage schema changes in a SQLite database by registering migrations for specific
///     versions and applying them in sequence. Migrations are executed asynchronously and must be run prior to
///     initializing any cache-related services to guarantee schema consistency. Only one migration can be
///     registered per version, and migrations are applied in ascending version order.
/// </remarks>
public static partial class SqliteMigrationRunner
{
    private static readonly Lock RegistrationLock = new();
    private static readonly SortedList<Version, Func<ISqliteMigration>> Migrations = new();

    /// <summary>
    ///     Registers a migration for the specified database version. The migration is instantiated using a parameterless
    ///     constructor.
    /// </summary>
    /// <remarks>
    ///     Only one migration can be registered per version. Re-registering the same version is a no-op so the
    ///     runner is safe to call multiple times in one process.
    /// </remarks>
    /// <typeparam name="TMigration">
    ///     The type of migration to register. Must implement the ISqliteMigration interface and provide a public
    ///     parameterless constructor.
    /// </typeparam>
    /// <param name="targetVersion">The database version to associate with the migration. Cannot be null.</param>
    public static void Register<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        TMigration>(
        Version targetVersion)
        where TMigration : ISqliteMigration, new()
    {
        ArgumentNullException.ThrowIfNull(targetVersion);

        lock (RegistrationLock)
        {
            // Skip if already registered for this version (idempotent).
            if (Migrations.ContainsKey(targetVersion))
            {
                return;
            }

            Migrations.Add(targetVersion, static () => new TMigration());
        }
    }

    /// <summary>
    ///     The highest migration version currently registered, or <c>null</c> when none are
    ///     registered yet. Synchronous and DB-free, so startup-task conditions can compare it
    ///     against the persisted "already applied" version without opening a connection. Callers
    ///     must ensure migrations are registered first (see
    ///     <c>SqliteDatabaseService.EnsureMigrationsRegistered</c>).
    /// </summary>
    public static Version? GetLatestRegisteredVersion()
    {
        lock (RegistrationLock)
        {
            // Migrations is a SortedList<Version, …>, so its keys are kept in ascending order
            // (Register inserts each into sorted position regardless of call order). The last key
            // is therefore the highest registered version — an O(1) read, no scan/compare needed.
            return Migrations.Count == 0 ? null : Migrations.Keys[^1];
        }
    }

    /// <summary>
    ///     Applies all pending SQLite database migrations whose target version is greater than the current database
    ///     version.
    /// </summary>
    /// <remarks>
    ///     Always-call contract: this method MUST run on every cold start that has an active user partition. It is
    ///     idempotent — when there are no pending migrations it short-circuits after a single SELECT against the
    ///     bookkeeping table. Do NOT gate it behind app-version preferences (those gates can hide newly-registered
    ///     migrations from existing installs). See <c>AiGuides/05-Client-Migrations.md</c>.
    /// </remarks>
    /// <param name="connectionProvider">
    ///     The provider for the SQLite connection used to execute the migrations. Must not be null.
    /// </param>
    /// <param name="logger">Logger used to emit structured boundary events.</param>
    /// <returns>A task that represents the asynchronous operation of applying the migrations.</returns>
    public static async Task RunAsync(G9SqliteConnectionProvider connectionProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(connectionProvider);
        ArgumentNullException.ThrowIfNull(logger);

        KeyValuePair<Version, Func<ISqliteMigration>>[] snapshot;
        lock (RegistrationLock)
        {
            if (Migrations.Count == 0)
            {
                LogNoMigrationsRegistered(logger);
                return;
            }

            snapshot = Migrations.ToArray();
        }

        var db = connectionProvider.Connection;

        await EnsureVersionTableAsync(db).ConfigureAwait(false);

        var currentVersion = await ReadCurrentVersionAsync(db).ConfigureAwait(false);

        var pending = snapshot.Where(kvp => kvp.Key > currentVersion).ToArray();
        if (pending.Length == 0)
        {
            LogNoPendingMigrations(logger, currentVersion.ToString(), snapshot.Length);
            return;
        }

        LogRunnerStart(logger, currentVersion.ToString(), pending.Length, snapshot.Length);

        var totalSw = Stopwatch.StartNew();
        var applied = 0;

        foreach (var (targetVersion, factory) in pending)
        {
            var migration = factory();
            var migrationName = migration.GetType().Name;
            var sw = Stopwatch.StartNew();

            LogMigrationApplying(logger, targetVersion.ToString(), migrationName);

            try
            {
                await migration.MigrateAsync(db, logger).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogMigrationFailed(logger, ex, targetVersion.ToString(), migrationName, sw.ElapsedMilliseconds);
                throw;
            }

            await db.ExecuteAsync(
                "UPDATE [__SqliteMigrationVersion] SET [Version] = ? WHERE [Id] = 1",
                targetVersion.ToString()).ConfigureAwait(false);

            sw.Stop();
            applied++;
            LogMigrationApplied(logger, targetVersion.ToString(), migrationName, sw.ElapsedMilliseconds);
        }

        totalSw.Stop();
        LogRunnerComplete(logger, applied, totalSw.ElapsedMilliseconds);
    }

    /// <summary>
    ///     Ensures that the migration version tracking table exists in the SQLite database. Creates the table if it does
    ///     not already exist.
    /// </summary>
    private static Task EnsureVersionTableAsync(SQLiteAsyncConnection db)
    {
        return db.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS [__SqliteMigrationVersion] " +
            "([Id] INTEGER PRIMARY KEY, [Version] TEXT NOT NULL)");
    }

    /// <summary>
    ///     Retrieves the current migration version of the SQLite database, or initializes it to a default version if no
    ///     version record exists.
    /// </summary>
    private static async Task<Version> ReadCurrentVersionAsync(SQLiteAsyncConnection db)
    {
        var versionText = await db.ExecuteScalarAsync<string?>(
            "SELECT [Version] FROM [__SqliteMigrationVersion] WHERE [Id] = 1").ConfigureAwait(false);

        if (versionText is not null)
        {
            return Version.Parse(versionText);
        }

        var defaultVersion = new Version(1, 0, 0, 0);
        await db.ExecuteAsync(
            "INSERT INTO [__SqliteMigrationVersion] ([Id], [Version]) VALUES (1, ?)",
            defaultVersion.ToString()).ConfigureAwait(false);

        return defaultVersion;
    }

    #region Logs

    [LoggerMessage(5100, LogLevel.Information,
        "Migration runner: starting (current={CurrentVersion}, pending={PendingCount}/{TotalRegistered})")]
    private static partial void LogRunnerStart(
        ILogger logger,
        string currentVersion,
        int pendingCount,
        int totalRegistered);

    [LoggerMessage(5101, LogLevel.Information,
        "Migration runner: completed ({AppliedCount} applied in {ElapsedMs}ms)")]
    private static partial void LogRunnerComplete(ILogger logger, int appliedCount, long elapsedMs);

    [LoggerMessage(5102, LogLevel.Debug,
        "Migration runner: no pending migrations (current={CurrentVersion}, registered={TotalRegistered})")]
    private static partial void LogNoPendingMigrations(ILogger logger, string currentVersion, int totalRegistered);

    [LoggerMessage(5103, LogLevel.Debug, "Migration runner: no migrations registered")]
    private static partial void LogNoMigrationsRegistered(ILogger logger);

    [LoggerMessage(5110, LogLevel.Information, "Migration applying {Version} ({Name})")]
    private static partial void LogMigrationApplying(ILogger logger, string version, string name);

    [LoggerMessage(5111, LogLevel.Information, "Migration applied {Version} ({Name}) in {ElapsedMs}ms")]
    private static partial void LogMigrationApplied(ILogger logger, string version, string name, long elapsedMs);

    [LoggerMessage(5112, LogLevel.Error, "Migration failed {Version} ({Name}) after {ElapsedMs}ms")]
    private static partial void LogMigrationFailed(
        ILogger logger,
        Exception ex,
        string version,
        string name,
        long elapsedMs);

    #endregion
}
