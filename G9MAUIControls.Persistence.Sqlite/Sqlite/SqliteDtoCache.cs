using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Provides a static cache for custom Data Transfer Object (DTO) projections derived from an entity's underlying
///     table,
///     allowing efficient retrieval and management of cached data.
/// </summary>
/// <remarks>
///     This class allows defining a cache for DTOs that can be refreshed based on changes to the underlying
///     entity. It supports asynchronous operations for cache retrieval and refresh, and allows listeners to be notified of
///     cache updates.
/// </remarks>
/// <typeparam name="TEntity">
///     The type of the entity from which the DTO is derived. This type must be a class with a
///     parameterless constructor.
/// </typeparam>
/// <typeparam name="TDto">
///     The type of the Data Transfer Object (DTO) that represents the shape of the cached projection. This type must be a
///     class with a parameterless constructor.
/// </typeparam>
public static partial class SqliteDtoCache<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    TEntity,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    TDto>
    where TEntity : class, new()
    where TDto : class, new()
{
    private static readonly Lock CacheStateLock = new();
    private static readonly Lock CacheListenerLock = new();
    private static readonly SemaphoreSlim CacheRefreshLock = new(1, 1);
    private static readonly TimeSpan DefaultRefreshGap = TimeSpan.FromMilliseconds(300);

    private static Func<SqliteRepository<TEntity>, Task<List<TDto>>>? QueryFactory;
    private static G9SqliteConnectionProvider? CacheProvider;

    // Held alongside the provider because the cache constructs its own repository to run the refresh
    // query, and the repository now needs the frozen options (clock, current user, descriptors).
    private static G9SqliteOptions? CacheOptions;
    private static List<TDto>? CacheRows;
    private static readonly List<WeakReference<Action<List<TDto>>>> CacheListeners = [];
    private static CancellationTokenSource? CacheDebounceCts;
    private static bool CacheDefined;
    private static bool CacheInitialized;
    private static bool EmptyCacheRetryAttempted;
    private static TimeSpan CacheRefreshGap = DefaultRefreshGap;

    /// <summary>
    ///     Gets a value indicating whether a cache has been defined.
    /// </summary>
    /// <remarks>
    ///     This property is thread-safe. Access to the cache state is synchronized to ensure consistent
    ///     results when used from multiple threads.
    /// </remarks>
    public static bool HasDefinedCache
    {
        get
        {
            lock (CacheStateLock)
            {
                return CacheDefined;
            }
        }
    }

    /// <summary>
    ///     Defines and initializes a cache for data transfer objects (DTOs) using a specified query factory and connection
    ///     provider.
    /// </summary>
    /// <remarks>
    ///     This method registers the DTO cache for the specified entity type, sets the default refresh
    ///     interval, and immediately refreshes the cache. Subsequent cache refreshes will use the provided query
    ///     factory.
    /// </remarks>
    /// <param name="connectionProvider">The provider used to establish connections to the SQLite database. Cannot be null.</param>
    /// <param name="queryFactory">
    ///     A function that receives a repository for the entity type and returns a task that produces a list of projected
    ///     DTOs. Cannot be null.
    /// </param>
    /// <returns>A task that represents the asynchronous operation of defining and refreshing the cache.</returns>
    public static async Task DefineCache(
        G9SqliteConnectionProvider connectionProvider,
        G9SqliteOptions options,
        Func<SqliteRepository<TEntity>, Task<List<TDto>>> queryFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(queryFactory);

        lock (CacheStateLock)
        {
            CacheDefined = true;
            CacheProvider = connectionProvider;
            CacheOptions = options;
            QueryFactory = queryFactory;
            CacheRefreshGap = DefaultRefreshGap;
        }

        SqliteRepositoryCacheRegistry.RegisterDtoCacheForEntity(
            typeof(TEntity),
            typeof(SqliteDtoCache<TEntity, TDto>),
            RefreshCache,
            ResetCacheForSession);

        await RefreshCache().ConfigureAwait(false);
    }

    /// <summary>
    ///     Refreshes the cache asynchronously, ensuring that the cache is initialized and any pending refresh operations
    ///     are canceled before updating.
    /// </summary>
    /// <remarks>
    ///     Call this method when the cache needs to be updated. Any previous refresh requests are
    ///     canceled to prevent redundant operations. This method should be awaited to ensure the cache is fully refreshed
    ///     before proceeding with dependent actions.
    /// </remarks>
    /// <returns>A task that represents the asynchronous cache refresh operation.</returns>
    public static async Task RefreshCache()
    {
        EnsureCacheIsDefined();
        CancelPendingDebouncedRefresh();
        await RefreshCacheCoreAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Retrieves cached data synchronously as a list of DTOs.
    /// </summary>
    /// <remarks>
    ///     This method blocks the calling thread until the asynchronous operation completes. It is
    ///     recommended to use the asynchronous version, GetCacheDataAsync, to avoid blocking.
    /// </remarks>
    /// <returns>A list of DTOs containing the cached data. The list will be empty if no data is cached.</returns>
    public static List<TDto> GetCacheData()
    {
        return GetCacheDataAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>
    ///     Retrieves the cached data asynchronously, ensuring the cache is initialized before returning the results.
    /// </summary>
    /// <remarks>
    ///     If the cache is not initialized, this method refreshes the cache before retrieving the data.
    ///     If the cache is already initialized, it waits for any ongoing refresh operations to complete before returning
    ///     the data. This method is thread-safe and can be called concurrently without risk of inconsistent cache
    ///     state.
    /// </remarks>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a list of cached data of type
    ///     TDto.
    /// </returns>
    public static async Task<List<TDto>> GetCacheDataAsync()
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
    ///     Registers a listener that is notified with the current cache data when it becomes available.
    /// </summary>
    /// <remarks>
    ///     Listeners are stored as weak references to prevent memory leaks. If the listener is no longer
    ///     referenced elsewhere, it may be garbage collected.
    /// </remarks>
    /// <param name="listener">
    ///     The action to invoke with the current cache data, represented as a list of DTOs. This parameter
    ///     cannot be null.
    /// </param>
    public static void ListenToCacheData(Action<List<TDto>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (CacheListenerLock)
        {
            CacheListeners.Add(new WeakReference<Action<List<TDto>>>(listener));
        }

        if (TryGetCacheRows(out var cacheRows))
        {
            SafeInvokeListener(listener, cacheRows);
        }
    }

    /// <summary>
    ///     Schedules a debounced refresh operation for the cache, consolidating multiple refresh requests within a short
    ///     interval into a single asynchronous refresh.
    /// </summary>
    /// <remarks>
    ///     This method cancels any previously scheduled refresh operation to avoid redundant processing.
    ///     The cache must be defined and the cache provider available for the refresh to be scheduled. The refresh is
    ///     executed asynchronously after a delay specified by the cache refresh gap.
    /// </remarks>
    public static void ScheduleDebouncedRefresh()
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

        previousDebounceCts?.Cancel();
        G9SqliteFireAndForget.Run(() => RunDebouncedRefreshAsync(debounceCts, refreshGap));
    }
}
