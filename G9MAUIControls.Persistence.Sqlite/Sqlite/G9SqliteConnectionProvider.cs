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

    // The opt-in made by ApplyPerformancePragmasAsync, remembered for the life of the provider so every
    // LATER connection gets the same PRAGMAs. Volatile because it is read from sqlite-net's open callback,
    // which runs on whichever thread-pool thread performs a connection's first operation — outside the lock.
    private volatile bool _performancePragmasRequested;

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
            SQLiteAsyncConnection connection;
            SQLiteAsyncConnection? staleConnection = null;

            lock (_connectionLock)
            {
                // Read INSIDE the lock. Read outside it, a thread could resolve user A's path, lose the CPU
                // while a switch to user B completed, then come back, find "B is open but I was told A",
                // close B and reopen A — after which the new session's writes land in the previous user's
                // file. Inside the lock, the path and the decision made on it are one atomic step.
                var path = _locator.GetDatabasePath();

                if (_connection is not null &&
                    string.Equals(_databasePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return _connection;
                }

                if (_connection is not null)
                {
                    // Detached here, CLOSED BELOW, outside the lock. Closing waits on SQLite (a WAL
                    // checkpoint, on the last connection to a file); blocking on that while holding the lock
                    // stalled every other caller behind it, on whatever thread they were — the UI thread
                    // included.
                    staleConnection = _connection;
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
                //
                // postKeyAction is sqlite-net's "the native handle has just been opened" hook. busy_timeout,
                // synchronous, cache_size and friends belong to a HANDLE, not to the database file, so they
                // were lost on every reconnect — a user switch, a restore, any close-and-reopen — long after
                // ApplyPerformancePragmasAsync had returned. Re-applying them from this hook covers every
                // handle this provider opens, before the first statement runs on it.
                var connectionString = new SQLiteConnectionString(
                    path,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create,
                    false,
                    postKeyAction: ApplyRequestedPragmasOnOpen,
                    dateTimeStringFormat: LegacyDateTimeTextFormat);

                _connection = new SQLiteAsyncConnection(connectionString);
                _databasePath = path;
                connection = _connection;
            }

            if (staleConnection is not null)
            {
                try
                {
                    staleConnection.CloseAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // Same posture as OnDatabasePathChanged. It is already detached, so it can never be
                    // handed out again, and failing the caller's query over the PREVIOUS database's close
                    // helps nobody. (It used to throw from inside the lock with _connection still pointing
                    // at the stale connection, so every later caller retried the same failing close.)
                }

                // A swap noticed here is the same event as DatabasePathChanged and needs the same
                // consequence. It was skipped, so a locator that changed its answer without raising the event
                // left the previous database's rows in every cache. Outside the lock: a reset calls listeners.
                SqliteRepositoryCacheRegistry.ResetAllCachesForSession();
            }

            return connection;
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
    /// <remarks>
    ///     <b>The opt-in is remembered.</b> Calling this once applies the PRAGMAs to the connection that is
    ///     open now, and the provider re-applies them to every connection it opens afterwards — after a user
    ///     switch, a restore, or any close-and-reopen. All of them except the journal mode are
    ///     per-connection settings, so until this was remembered the first reconnect silently fell back to
    ///     SQLite's defaults, including sqlite-net's one-second busy timeout. With no connection open yet,
    ///     the call just records the opt-in and the next connection picks it up.
    /// </remarks>
    public async Task ApplyPerformancePragmasAsync()
    {
        SQLiteAsyncConnection connection;
        lock (_connectionLock)
        {
            _performancePragmasRequested = true;

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

    /// <summary>
    ///     Closes the open connection and resets every repository/DTO cache, without blocking the caller.
    ///     The awaitable form of what <see cref="IG9SqliteDatabaseLocator.DatabasePathChanged" /> triggers.
    /// </summary>
    /// <remarks>
    ///     Await this at a session boundary — sign-out, user switch, before replacing the database file —
    ///     <b>before</b> the locator starts returning a different path. The <c>DatabasePathChanged</c>
    ///     handler has to be synchronous (the event is an <see cref="EventHandler" />), so it can only wait
    ///     for the close by blocking the thread that raised the event, which at sign-out is normally the UI
    ///     thread. Once this has run there is no connection left for that handler to close and it returns
    ///     immediately. The next <see cref="Connection" /> access opens whatever file the locator names
    ///     then, with the requested PRAGMAs re-applied.
    /// </remarks>
    public async Task SwitchDatabaseAsync()
    {
        try
        {
            await CloseCurrentConnectionAsync().ConfigureAwait(false);
        }
        finally
        {
            // Unconditional, as in OnDatabasePathChanged: a failed close is loud on the next query, a stale
            // cached row is silent.
            SqliteRepositoryCacheRegistry.ResetAllCachesForSession();
        }
    }

    /// <summary>
    ///     sqlite-net's open hook: runs on every native handle this provider's connections open, right after
    ///     the open and before any statement. Mirrors <see cref="ApplyPerformancePragmasAsync" /> — keep the
    ///     two lists identical.
    /// </summary>
    /// <remarks>
    ///     Best-effort by design. These are tuning, and the hook runs inside sqlite-net's connection
    ///     constructor: a throw here would make the DATABASE unopenable over a setting that only makes it
    ///     faster (switching to WAL, for one, needs a lock another connection may be holding). A PRAGMA that
    ///     fails is skipped and the connection still opens, with SQLite's default for that setting.
    /// </remarks>
    private void ApplyRequestedPragmasOnOpen(SQLiteConnection connection)
    {
        if (!_performancePragmasRequested)
        {
            return;
        }

        // ExecuteScalar, never Execute — see ApplyPerformancePragmasAsync for why.
        TryPragma<string>(connection, "PRAGMA journal_mode=WAL");
        TryPragma<int>(connection, "PRAGMA synchronous=NORMAL");
        TryPragma<int>(connection, "PRAGMA temp_store=MEMORY");
        TryPragma<long>(connection, "PRAGMA cache_size=-40000");
        TryPragma<long>(connection, "PRAGMA mmap_size=134217728");
        TryPragma<int>(connection, "PRAGMA busy_timeout=5000");

        static void TryPragma<TResult>(SQLiteConnection connection, string pragma)
        {
            try
            {
                connection.ExecuteScalar<TResult>(pragma);
            }
            catch (Exception)
            {
                // See the remarks: never fail an open over tuning.
            }
        }
    }
}
