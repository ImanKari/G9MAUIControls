using Microsoft.Maui.Storage;
using SQLite;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Provides an asynchronous connection to a SQLite database, allowing for read and write operations.
/// </summary>
/// <remarks>
///     This class manages the connection to the SQLite database specified by the path in the application
///     settings. It ensures that datetime values are correctly parsed from the legacy text format used in existing
///     databases. The connection is established lazily, meaning it is created only when accessed for the first
///     time.
/// </remarks>
// Renamed and re-pointed on extraction: the app injected its own per-user partition service; the
// package takes the locator STRATEGY instead, so single-file, per-user, per-tenant and in-memory are all
// expressible without the package knowing which one is in use.
public sealed class G9SqliteConnectionProvider : IAsyncDisposable
{
    private const string LegacyDateTimeTextFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";
    private readonly Lock _connectionLock = new();
    private readonly IG9SqliteDatabaseLocator _locator;

    private SQLiteAsyncConnection? _connection;
    private string? _databasePath;
    private bool _pragmasApplied;

    /// <summary>Creates the provider over a database locator.</summary>
    /// <param name="locator">Decides which file is open, and says when the answer changes.</param>
    /// <remarks>
    ///     <b>Subscribes to <see cref="IG9SqliteDatabaseLocator.DatabasePathChanged" />.</b> Without
    ///     that, the interface's own contract — "the provider closes the current connection and resets
    ///     every cache" — held only by luck: the path is re-read on every
    ///     <see cref="Connection" /> acquisition, so a swap was noticed by the NEXT caller, and any
    ///     already-handed-out <see cref="SQLiteAsyncConnection" /> kept writing to the previous user's
    ///     file. Closing eagerly on the signal is what makes the guarantee real.
    /// </remarks>
    public G9SqliteConnectionProvider(IG9SqliteDatabaseLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
        _locator.DatabasePathChanged += OnDatabasePathChanged;
    }

    /// <summary>
    ///     Closes the open connection and drops every repository/DTO cache, so nothing from the previous
    ///     database can be served out of memory against the next one.
    /// </summary>
    /// <remarks>
    ///     Synchronous by necessity — the event is <see cref="EventHandler" /> — and best-effort:
    ///     a locator that announces a change must not be able to throw out of its own raise loop and
    ///     leave later subscribers unnotified. The cache reset is unconditional even if the close fails,
    ///     because serving a stale row is the failure that silently corrupts data, while a stuck
    ///     connection surfaces loudly on the next query.
    /// </remarks>
    private void OnDatabasePathChanged(object? sender, EventArgs e)
    {
        try
        {
            CloseCurrentConnectionAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            lock (_connectionLock)
            {
                _connection = null;
                _databasePath = null;
                _pragmasApplied = false;
            }
        }

        SqliteRepositoryCacheRegistry.ResetAllCachesForSession();
    }

    /// <summary>
    ///     Gets the SQLite asynchronous connection used for database operations.
    /// </summary>
    /// <remarks>
    ///     This property provides access to the underlying SQLite connection, which is used to perform
    ///     asynchronous database operations. Ensure that the connection is properly initialized before use.
    /// </remarks>
    public SQLiteAsyncConnection Connection
    {
        get
        {
            var path = _locator.GetDatabasePath();
            lock (_connectionLock)
            {
                if (_connection is not null &&
                    string.Equals(_databasePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return _connection;
                }

                if (_connection is not null)
                {
                    _connection.CloseAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    _connection = null;
                    _databasePath = null;
                    _pragmasApplied = false;
                }

                // The locator owns WHERE; the provider owns making sure it exists. Deriving the directory from
                // the path keeps the locator interface to a single question.
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Existing databases store datetime columns as TEXT values in a space-separated ISO style.
                // Configure sqlite-net to parse this format with invariant culture to avoid culture-specific failures.
                var connectionString = new SQLiteConnectionString(
                    path,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create,
                    false,
                    dateTimeStringFormat: LegacyDateTimeTextFormat);

                _connection = new SQLiteAsyncConnection(connectionString);
                _databasePath = path;
                return _connection;
            }
        }
    }

    /// <summary>
    ///     Gets the path to the database file used by the application.
    /// </summary>
    /// <remarks>
    ///     This property provides the location of the database file, which is essential for database
    ///     operations. Ensure that the path is valid and accessible to avoid runtime errors.
    /// </remarks>
    public string DatabasePath => _locator.GetDatabasePath();

    /// <summary>The path of the OPEN connection, or <c>null</c> when none is open yet.</summary>
    public string? ActiveDatabasePath
    {
        get { lock (_connectionLock) { return _databasePath; } }
    }

    /// <summary>
    ///     Whether a database path can currently be resolved.
    /// </summary>
    /// <remarks>
    ///     A locator is allowed to THROW when it has no answer — G9PerUserDatabaseLocator does exactly that
    ///     when nobody is signed in, deliberately, so a write can never land in a shared file by accident.
    ///     This turns that into a question a caller can ask without a try/catch at the call site.
    /// </remarks>
    public bool HasActiveDatabase
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(_locator.GetDatabasePath());
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Unsubscribes from the locator first. The locator normally outlives the provider (it is the
    ///     app's own service), so leaving the handler attached would keep a disposed provider reachable
    ///     and let a later path change reach into it.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        _locator.DatabasePathChanged -= OnDatabasePathChanged;
        await CloseCurrentConnectionAsync().ConfigureAwait(false);
    }

    public async Task CheckpointWalAsync()
    {
        SQLiteAsyncConnection? connection;
        lock (_connectionLock)
        {
            connection = _connection;
        }

        if (connection is null)
        {
            return;
        }

        await connection.ExecuteScalarAsync<int>("PRAGMA wal_checkpoint(TRUNCATE)").ConfigureAwait(false);
    }

    /// <summary>
    ///     Applies performance-oriented PRAGMAs to the sqlite-net connection. Called once after the
    ///     database is first accessed (e.g. after migrations). These settings mirror the read-friendly
    ///     subset of <c>TunedSqliteSyncProvider</c> and are safe across all platforms:
    ///     <list type="bullet">
    ///         <item><description>WAL journaling for concurrent read/write.</description></item>
    ///         <item><description>NORMAL synchronous mode (good crash safety, much faster than FULL).</description></item>
    ///         <item><description>MEMORY temp store (fewer fsyncs).</description></item>
    ///         <item><description>40 MB page cache (reduces re-fetches in long-running batches).</description></item>
    ///         <item><description>128 MB mmap window (zero-copy reads for hot pages).</description></item>
    ///         <item><description>5 s busy timeout (ride out short locks instead of throwing).</description></item>
    ///     </list>
    /// </summary>
    public async Task ApplyPerformancePragmasAsync()
    {
        SQLiteAsyncConnection connection;
        lock (_connectionLock)
        {
            if (_pragmasApplied || _connection is null)
            {
                return;
            }

            connection = _connection;
        }

        // Each PRAGMA must be a separate call because sqlite-net does not support
        // multi-statement execution. Order does not matter for these settings.
        // PRAGMAs that return a result row (e.g. journal_mode, mmap_size, busy_timeout) MUST
        // use ExecuteScalarAsync; sqlite-net's ExecuteNonQuery treats SQLITE_ROW as an error
        // and throws SQLiteException("not an error"). ExecuteScalarAsync handles both row and
        // no-row results, so it's safe for every PRAGMA.
        await connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL").ConfigureAwait(false);
        await connection.ExecuteScalarAsync<int>("PRAGMA synchronous=NORMAL").ConfigureAwait(false);
        await connection.ExecuteScalarAsync<int>("PRAGMA temp_store=MEMORY").ConfigureAwait(false);
        await connection.ExecuteScalarAsync<long>("PRAGMA cache_size=-40000").ConfigureAwait(false);
        await connection.ExecuteScalarAsync<long>("PRAGMA mmap_size=134217728").ConfigureAwait(false);
        await connection.ExecuteScalarAsync<int>("PRAGMA busy_timeout=5000").ConfigureAwait(false);

        lock (_connectionLock)
        {
            _pragmasApplied = true;
        }
    }

    public async Task CloseCurrentConnectionAsync()
    {
        SQLiteAsyncConnection? connection;
        lock (_connectionLock)
        {
            connection = _connection;
            _connection = null;
            _databasePath = null;
            _pragmasApplied = false;
        }

        if (connection is not null)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}
