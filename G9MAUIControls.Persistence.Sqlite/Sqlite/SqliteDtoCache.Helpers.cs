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

        debounceCts?.Cancel();
    }

    private static void ResetCacheForSession()
    {
        CancellationTokenSource? debounceCts;
        lock (CacheStateLock)
        {
            debounceCts = CacheDebounceCts;
            CacheDebounceCts = null;
            CacheProvider = null;
            CacheRows = null;
            CacheInitialized = false;
            EmptyCacheRetryAttempted = false;
        }

        debounceCts?.Cancel();
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

        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var repository = new SqliteRepository<TEntity>(provider, options);
            var cacheRows = await queryFactory(repository).ConfigureAwait(false);

            lock (CacheStateLock)
            {
                CacheRows = cacheRows;
                CacheInitialized = true;
                if (cacheRows.Count > 0)
                {
                    EmptyCacheRetryAttempted = false;
                }
            }

            NotifyCacheListeners(cacheRows);
        }
        catch (SQLiteException ex) when (
            ex.Result == SQLite3.Result.Error
            && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            lock (CacheStateLock)
            {
                CacheRows = [];
                CacheInitialized = true;
            }

            NotifyCacheListeners([]);
        }
        finally
        {
            CacheRefreshLock.Release();
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
        var listeners = GetAliveListeners();
        if (listeners.Length == 0)
        {
            return;
        }

        foreach (var listener in listeners)
        {
            SafeInvokeListener(listener, cacheRows);
        }
    }

    private static Action<List<TDto>>[] GetAliveListeners()
    {
        lock (CacheListenerLock)
        {
            if (CacheListeners.Count == 0)
            {
                return [];
            }

            var aliveListeners = new List<Action<List<TDto>>>(CacheListeners.Count);
            for (var i = CacheListeners.Count - 1; i >= 0; i--)
            {
                if (CacheListeners[i].TryGetTarget(out var listener))
                {
                    aliveListeners.Add(listener);
                }
                else
                {
                    CacheListeners.RemoveAt(i);
                }
            }

            aliveListeners.Reverse();
            return aliveListeners.ToArray();
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
