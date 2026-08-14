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
            CacheConnection = connectionProvider.Connection;
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

    public static void ListenToCacheData(Action<List<T>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (CacheListenerLock)
        {
            CacheListeners.Add(new WeakReference<Action<List<T>>>(listener));
        }

        if (TryGetCacheRows(out var cacheRows))
        {
            SafeInvokeListener(listener, cacheRows);
        }
    }

    private void InitializeCacheForInstance()
    {
        if (!HasDefinedCache)
        {
            return;
        }

        EnsureCacheConnection(Db);
    }

    private static void EnsureCacheIsDefined()
    {
        if (!HasDefinedCache)
        {
            throw new InvalidOperationException(
                $"Cache is not defined for entity type '{typeof(T).Name}'. Call {nameof(DefineCache)} first.");
        }
    }

    private static void EnsureCacheConnection(SQLiteAsyncConnection connection)
    {
        lock (CacheStateLock)
        {
            CacheConnection = connection;
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

        EnsureCacheConnection(Db);
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
            if (!CacheDefined || CacheConnection is null)
            {
                return;
            }

            previousDebounceCts = CacheDebounceCts;
            debounceCts = new CancellationTokenSource();
            CacheDebounceCts = debounceCts;
            refreshGap = CacheRefreshGap;
        }

        previousDebounceCts?.Cancel();
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

        debounceCts?.Cancel();
    }

    private static void ResetCacheForSession()
    {
        CancellationTokenSource? debounceCts;
        lock (CacheStateLock)
        {
            debounceCts = CacheDebounceCts;
            CacheDebounceCts = null;
            CacheConnection = null;
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
        SQLiteAsyncConnection connection;
        lock (CacheStateLock)
        {
            if (!CacheDefined)
            {
                return;
            }

            connection = CacheConnection ?? throw new InvalidOperationException(
                $"Cache is defined for entity type '{typeof(T).Name}' but no SQLite connection is available.");
        }

        await CacheRefreshLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cacheRows = await LoadCacheRowsAsync(connection).ConfigureAwait(false);
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
        catch (SQLiteException ex) when (IsTableNotFoundError(ex))
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

    private static void NotifyCacheListeners(List<T> cacheRows)
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

    private static Action<List<T>>[] GetAliveListeners()
    {
        lock (CacheListenerLock)
        {
            if (CacheListeners.Count == 0)
            {
                return [];
            }

            var aliveListeners = new List<Action<List<T>>>(CacheListeners.Count);
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
