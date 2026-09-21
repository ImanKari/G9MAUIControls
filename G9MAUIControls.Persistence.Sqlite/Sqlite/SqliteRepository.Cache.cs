using G9MAUIControls.Persistence.Sqlite.Queries;
using SQLite;
using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

public partial class SqliteRepository<T> where T : class, new()
{
    private static bool IsCacheInitialized
    {
        get
        {
            lock (CacheStateLock)
            {
                return CacheInitialized;
            }
        }
    }

    public static async Task DefineCache(G9SqliteConnectionProvider connectionProvider)
    {
        ArgumentNullException.ThrowIfNull(connectionProvider);

        lock (CacheStateLock)
        {
            CacheDefined = true;
            CacheProvider = connectionProvider;
            CacheRefreshGap = DefaultRefreshGap;
        }

        SqliteRepositoryCacheRegistry.Register(typeof(T), RefreshCache, ResetCacheForSession);
        await RefreshCache().ConfigureAwait(false);
    }

    public static async Task RefreshCache()
    {
        EnsureCacheIsDefined();
        CancelPendingDebouncedRefresh();
        await RefreshCacheCoreAsync().ConfigureAwait(false);
    }

    public static Task HardRefreshAllCache()
    {
        return SqliteRepositoryCacheRegistry.HardRefreshAllCacheAsync();
    }

    public static List<T> GetCacheData()
    {
        return GetCacheDataAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public static async Task<List<T>> GetCacheDataAsync()
    {
        EnsureCacheIsDefined();

        if (!IsCacheInitialized)
        {
            await RefreshCache().ConfigureAwait(false);
        }
        else
        {
            await WaitForRefreshCompletionAsync().ConfigureAwait(false);
        }

        var rows = CopyCacheRows();
        if (rows.Count > 0 || !TryMarkEmptyCacheRetryNeeded())
        {
            return rows;
        }

        await RefreshCache().ConfigureAwait(false);
        return CopyCacheRows();
    }

    /// <summary>
    ///     Registers a listener that is called with a copy of the cached rows after every refresh, and with
    ///     an empty list when the cache is reset at a session boundary. If the cache is already populated
    ///     the listener is also called once, immediately.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>The listener is held strongly, like an ordinary event handler.</b> It — and whatever it
    ///         captures — stays alive until <see cref="StopListeningToCacheData" /> is called, so a
    ///         short-lived subscriber (a page, a view model) must unsubscribe when it goes away.
    ///     </para>
    ///     <para>
    ///         It used to be held through a weak reference to the DELEGATE. That did not tie the
    ///         subscription to the subscriber's lifetime as intended: the delegate object is referenced by
    ///         nothing else, so it was collected at the next GC and the listener silently stopped being
    ///         called while its owner was still alive.
    ///     </para>
    ///     <para>
    ///         Listeners run outside the cache's refresh lock, so calling
    ///         <see cref="GetCacheData" /> from inside one is safe.
    ///     </para>
    /// </remarks>
    public static void ListenToCacheData(Action<List<T>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (CacheListenerLock)
        {
            CacheListeners.Add(listener);
        }

        if (TryGetCacheRows(out var cacheRows))
        {
            SafeInvokeListener(listener, cacheRows);
        }
    }

    /// <summary>
    ///     Removes a listener added with <see cref="ListenToCacheData" />.
    /// </summary>
    /// <remarks>
    ///     Delegates compare by target and method, so passing the same method group again
    ///     (<c>StopListeningToCacheData(OnRows)</c>) removes it — the instance does not have to be kept.
    ///     A lambda must be kept in a field to be removable, exactly as with a C# event. When the same
    ///     listener was added more than once, one registration is removed per call.
    /// </remarks>
    /// <returns><c>true</c> when a registration was found and removed.</returns>
    public static bool StopListeningToCacheData(Action<List<T>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (CacheListenerLock)
        {
            var index = CacheListeners.LastIndexOf(listener);
            if (index < 0)
            {
                return false;
            }

            CacheListeners.RemoveAt(index);
            return true;
        }
    }

    private void InitializeCacheForInstance()
    {
        if (!HasDefinedCache)
        {
            return;
        }

        EnsureCacheProvider(_connectionProvider);
    }

    private static void EnsureCacheIsDefined()
    {
        if (!HasDefinedCache)
        {
            throw new InvalidOperationException(
                $"Cache is not defined for entity type '{typeof(T).Name}'. Call {nameof(DefineCache)} first.");
        }
    }

    // Records WHERE connections come from, without opening one. This used to store `Db` — which both
    // resolved the database path from a constructor (throwing when nobody is signed in) and pinned the
    // cache to whichever connection happened to be open at that moment.
    private static void EnsureCacheProvider(G9SqliteConnectionProvider connectionProvider)
    {
        lock (CacheStateLock)
        {
            CacheProvider = connectionProvider;
        }
    }

    private Task RefreshCacheAfterWriteAsync(int affectedRows, bool forceRefresh = false)
    {
        if (affectedRows <= 0 && !forceRefresh)
        {
            return Task.CompletedTask;
        }

        if (!HasDefinedCache)
        {
            return Task.CompletedTask;
        }

        EnsureCacheProvider(_connectionProvider);
        ScheduleDebouncedRefresh();
        return Task.CompletedTask;
    }

    private static async Task WaitForRefreshCompletionAsync()
    {
        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        CacheRefreshLock.Release();
    }

    private static void ScheduleDebouncedRefresh()
    {
        CancellationTokenSource debounceCts;
        CancellationTokenSource? previousDebounceCts;
        TimeSpan refreshGap;

        lock (CacheStateLock)
        {
            if (!CacheDefined || CacheProvider is null)
            {
                return;
            }

            previousDebounceCts = CacheDebounceCts;
            debounceCts = new CancellationTokenSource();
            CacheDebounceCts = debounceCts;
            refreshGap = CacheRefreshGap;
        }

        // Quietly: the previous debounce task may already have disposed its own source. This runs AFTER the
        // caller's row has been committed, so an ObjectDisposedException here would report a write that
        // succeeded as failed — and a caller that retries then inserts the row twice.
        G9SqliteFireAndForget.CancelQuietly(previousDebounceCts);
        G9SqliteFireAndForget.Run(() => RunDebouncedRefreshAsync(debounceCts, refreshGap));
    }

    private static void CancelPendingDebouncedRefresh()
    {
        CancellationTokenSource? debounceCts;
        lock (CacheStateLock)
        {
            debounceCts = CacheDebounceCts;
            CacheDebounceCts = null;
        }

        G9SqliteFireAndForget.CancelQuietly(debounceCts);
    }

    private static void ResetCacheForSession()
    {
        CancellationTokenSource? debounceCts;
        lock (CacheStateLock)
        {
            debounceCts = CacheDebounceCts;
            CacheDebounceCts = null;

            // Invalidates every refresh already in flight: it noted the old generation before resolving
            // its connection, so it will discard what it loaded instead of publishing the previous user's
            // rows into the next session. Cancelling the debounce below is not enough on its own — a
            // refresh that is past its delay, or one started by a read, is not stopped by it.
            CacheGeneration++;

            // CacheProvider is deliberately KEPT. CacheDefined stays true across a reset, so with the
            // connection source gone every read threw until some write or repository constructor happened
            // to put it back. The provider is not session state — it re-resolves the database per call.
            CacheRows = null;
            CacheInitialized = false;
            EmptyCacheRetryAttempted = false;
        }

        G9SqliteFireAndForget.CancelQuietly(debounceCts);
        NotifyCacheListeners([]);
    }

    private static async Task RunDebouncedRefreshAsync(CancellationTokenSource debounceCts, TimeSpan refreshGap)
    {
        try
        {
            if (refreshGap > TimeSpan.Zero)
            {
                await Task.Delay(refreshGap, debounceCts.Token).ConfigureAwait(false);
            }

            if (!debounceCts.Token.IsCancellationRequested)
            {
                await RefreshCacheCoreAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (CacheStateLock)
            {
                if (ReferenceEquals(CacheDebounceCts, debounceCts))
                {
                    CacheDebounceCts = null;
                }
            }

            debounceCts.Dispose();
        }
    }

    private static async Task RefreshCacheCoreAsync()
    {
        G9SqliteConnectionProvider provider;
        lock (CacheStateLock)
        {
            if (!CacheDefined)
            {
                return;
            }

            provider = CacheProvider ?? throw new InvalidOperationException(
                $"Cache is defined for entity type '{typeof(T).Name}' but no SQLite connection provider is available.");
        }

        var published = false;

        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // ORDER MATTERS: generation first, connection second. Resolved the other way round, a session
            // switch landing between the two would pair the OLD user's connection with the NEW generation,
            // and the check below would wave the previous user's rows through. Noted this way round, the
            // worst a badly-timed switch can do is make this refresh discard a perfectly good result.
            int generation;
            lock (CacheStateLock)
            {
                generation = CacheGeneration;
            }

            List<T> cacheRows;
            try
            {
                // Resolved NOW, from the provider — never captured earlier. A refresh can wait out a
                // debounce delay and then this lock, and a sign-out fits comfortably inside either.
                cacheRows = await LoadCacheRowsAsync(provider.Connection).ConfigureAwait(false);
            }
            catch (SQLiteException ex) when (IsTableNotFoundError(ex))
            {
                cacheRows = [];
            }

            lock (CacheStateLock)
            {
                if (generation == CacheGeneration)
                {
                    CacheRows = cacheRows;
                    CacheInitialized = true;
                    if (cacheRows.Count > 0)
                    {
                        EmptyCacheRetryAttempted = false;
                    }

                    published = true;
                }
            }
        }
        catch
        {
            // Mark the cache dirty. A debounced refresh's failure is swallowed by its runner, and with
            // CacheInitialized left true nothing would ever try again: every later read would serve the
            // rows from before the write that scheduled this refresh. Cleared, the next read refreshes —
            // and that one is awaited by its caller, so a persistent fault finally surfaces somewhere.
            lock (CacheStateLock)
            {
                CacheInitialized = false;
            }

            throw;
        }
        finally
        {
            CacheRefreshLock.Release();
        }

        if (published)
        {
            NotifyCacheListenersOfCurrentRows();
        }
    }

    private static bool IsTableNotFoundError(SQLiteException ex)
    {
        // Do not gate on ex.Result — the same "no such table" message can surface with
        // SQLITE_ERROR or SQLITE_SCHEMA depending on when the statement is prepared.
        return !string.IsNullOrEmpty(ex.Message)
               && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<List<T>> LoadCacheRowsAsync(SQLiteAsyncConnection connection)
    {
        var statement = SqliteQueryFactory.Select<T>()
            .WithCulture()
            .SelectAll()
            .BuildStatement();

        return connection.QueryAsync<T>(statement.Sql, statement.Parameters);
    }

    private static bool TryGetCacheRows(out List<T> cacheRows)
    {
        lock (CacheStateLock)
        {
            if (!CacheInitialized || CacheRows is null)
            {
                cacheRows = [];
                return false;
            }

            cacheRows = new List<T>(CacheRows);
            return true;
        }
    }

    private static List<T> CopyCacheRows()
    {
        lock (CacheStateLock)
        {
            return CacheRows is null ? [] : new List<T>(CacheRows);
        }
    }

    private static bool TryMarkEmptyCacheRetryNeeded()
    {
        lock (CacheStateLock)
        {
            if (CacheRows is not { Count: 0 } || EmptyCacheRetryAttempted)
            {
                return false;
            }

            EmptyCacheRetryAttempted = true;
            return true;
        }
    }

    /// <summary>
    ///     Tells listeners about a completed refresh. Called AFTER the refresh lock is released.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Listeners used to run inside the refresh lock, so one that called the synchronous
    ///         <see cref="GetCacheData" /> waited for a lock held by the very refresh that was calling it —
    ///         a deadlock with no timeout.
    ///     </para>
    ///     <para>
    ///         Outside the lock, two refreshes can reach this point out of order. Delivering the CURRENT
    ///         rows rather than the ones this refresh loaded makes that harmless: whichever notification
    ///         runs last still hands out the newest data. If a reset slipped in, there is nothing to
    ///         deliver — the reset has already told listeners the cache is empty.
    ///     </para>
    /// </remarks>
    private static void NotifyCacheListenersOfCurrentRows()
    {
        if (TryGetCacheRows(out var cacheRows))
        {
            NotifyCacheListeners(cacheRows);
        }
    }

    private static void NotifyCacheListeners(List<T> cacheRows)
    {
        var listeners = GetListenersSnapshot();
        if (listeners.Length == 0)
        {
            return;
        }

        foreach (var listener in listeners)
        {
            SafeInvokeListener(listener, cacheRows);
        }
    }

    // A snapshot, so a listener that subscribes or unsubscribes from inside its own callback cannot
    // invalidate the enumeration, and no listener runs while CacheListenerLock is held.
    private static Action<List<T>>[] GetListenersSnapshot()
    {
        lock (CacheListenerLock)
        {
            return CacheListeners.Count == 0 ? [] : CacheListeners.ToArray();
        }
    }

    private static void SafeInvokeListener(Action<List<T>> listener, List<T> cacheRows)
    {
        try
        {
            listener(new List<T>(cacheRows));
        }
        catch
        {
            // Listener errors are isolated from repository cache flow.
        }
    }
}
