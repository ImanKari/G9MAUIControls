using SQLite;

namespace G9MAUIControls.Persistence.Sqlite;

public static partial class SqliteDtoCache<
    TEntity,
    TDto>
    where TEntity : class, new()
    where TDto : class, new()
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

    private static void EnsureCacheIsDefined()
    {
        if (!HasDefinedCache)
        {
            throw new InvalidOperationException(
                $"DTO cache is not defined for <{typeof(TEntity).Name}, {typeof(TDto).Name}>. " +
                $"Call {nameof(DefineCache)} first.");
        }
    }

    private static async Task WaitForRefreshCompletionAsync()
    {
        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        CacheRefreshLock.Release();
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

            // Invalidates every refresh already in flight — it noted the old generation before querying,
            // so it discards its result instead of publishing the previous user's DTOs into the next
            // session. Cancelling the debounce below does not stop a refresh that is past its delay.
            CacheGeneration++;

            // CacheProvider is deliberately KEPT (it used to be nulled here). CacheDefined stays true
            // across a reset, so without a provider every read threw until DefineCache was called again.
            // The provider is not session state: it re-resolves the database on every acquisition.
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
        G9SqliteOptions options;
        Func<SqliteRepository<TEntity>, Task<List<TDto>>> queryFactory;

        lock (CacheStateLock)
        {
            if (!CacheDefined)
            {
                return;
            }

            options = CacheOptions ?? throw new InvalidOperationException(
                "SqliteDtoCache options are missing. DefineCache must be called with the options.");
            provider = CacheProvider ?? throw new InvalidOperationException(
                $"DTO cache is defined for <{typeof(TEntity).Name}, {typeof(TDto).Name}> " +
                "but no connection provider is available.");

            queryFactory = QueryFactory ?? throw new InvalidOperationException(
                $"DTO cache is defined for <{typeof(TEntity).Name}, {typeof(TDto).Name}> " +
                "but no query factory is available.");
        }

        var published = false;

        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Noted BEFORE the query resolves its connection (the repository asks the provider per call).
            // The other way round, a session switch between the two would pair the old user's connection
            // with the new generation and let the check below wave those rows through.
            int generation;
            lock (CacheStateLock)
            {
                generation = CacheGeneration;
            }

            List<TDto> cacheRows;
            try
            {
                var repository = new SqliteRepository<TEntity>(provider, options);
                cacheRows = await queryFactory(repository).ConfigureAwait(false);
            }
            catch (SQLiteException ex) when (
                ex.Result == SQLite3.Result.Error
                && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
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
            // CacheInitialized left true nothing would ever try again — reads would serve the pre-write
            // DTOs indefinitely. Cleared, the next read refreshes, and that one is awaited by its caller.
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

        // After the lock is released: a listener that calls the synchronous GetCacheData() would otherwise
        // wait on a lock held by the refresh that is calling it. The CURRENT rows are delivered rather than
        // the ones loaded above, so two refreshes notifying out of order still end on the newest data; if a
        // reset slipped in there is nothing to deliver, and the reset already announced "empty".
        if (published && TryGetCacheRows(out var currentRows))
        {
            NotifyCacheListeners(currentRows);
        }
    }

    private static bool TryGetCacheRows(out List<TDto> cacheRows)
    {
        lock (CacheStateLock)
        {
            if (!CacheInitialized || CacheRows is null)
            {
                cacheRows = [];
                return false;
            }

            cacheRows = new List<TDto>(CacheRows);
            return true;
        }
    }

    private static List<TDto> CopyCacheRows()
    {
        lock (CacheStateLock)
        {
            return CacheRows is null ? [] : new List<TDto>(CacheRows);
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

    private static void NotifyCacheListeners(List<TDto> cacheRows)
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
    private static Action<List<TDto>>[] GetListenersSnapshot()
    {
        lock (CacheListenerLock)
        {
            return CacheListeners.Count == 0 ? [] : CacheListeners.ToArray();
        }
    }

    private static void SafeInvokeListener(Action<List<TDto>> listener, List<TDto> cacheRows)
    {
        try
        {
            listener(new List<TDto>(cacheRows));
        }
        catch
        {
            // Listener errors are isolated from cache flow.
        }
    }
}
