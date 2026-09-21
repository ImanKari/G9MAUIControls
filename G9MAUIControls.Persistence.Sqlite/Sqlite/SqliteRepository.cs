using System.Diagnostics.CodeAnalysis;
using SQLite;
using System.Collections.Concurrent;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Provides a generic repository for performing asynchronous CRUD operations and queries on a SQLite database, with
///     optional caching of entity data.
/// </summary>
/// <remarks>
///     The repository supports batch operations, cache definition and refresh, and allows listeners to be
///     notified when cached data changes. It is designed for use with SQLite databases and exposes accessors for
///     selecting,
///     inserting, updating, deleting, and table operations. Caching is optional and can be defined per entity type to
///     improve performance for read-heavy scenarios.
/// </remarks>
/// <typeparam name="T">The type of the entity managed by the repository. Must be a class with a parameterless constructor.</typeparam>
/// <remarks>
///     <b>Marked <see cref="RequiresUnreferencedCodeAttribute" /> on the TYPE, on purpose.</b> sqlite-net
///     maps rows to objects by reflection over <typeparamref name="T" />, and this layer adds
///     expression-tree reflection on top. Neither can be expressed with
///     <see cref="DynamicallyAccessedMembersAttribute" /> alone.
///     <para>
///         Annotating the type propagates to every member, so a consumer publishing with trimming gets ONE
///         actionable warning naming this repository — rather than the unactionable
///         "assembly produced trim warnings" they would get if the annotation stopped at the assembly
///         boundary. Keep their entity types in an assembly listed in <c>TrimmerRootAssembly</c>.
///     </para>
///     <para>See ADR-0014. A source-generated mapper is the real fix and is a v2 conversation.</para>
/// </remarks>
[RequiresUnreferencedCode(
    "The SQLite repository maps rows by reflection over the entity type. Root your entity assembly with " +
    "TrimmerRootAssembly, or avoid trimming. See G9MAUIControls.Persistence.Sqlite ADR-0014.")]
public partial class SqliteRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T> where T : class, new()
{
    /// <summary>The frozen startup configuration: clock, current user, descriptors, interceptors.</summary>
    protected G9SqliteOptions Options { get; }

    public SqliteRepository(G9SqliteConnectionProvider connectionProvider, G9SqliteOptions options)
    {
        ArgumentNullException.ThrowIfNull(connectionProvider);
        ArgumentNullException.ThrowIfNull(options);
        _connectionProvider = connectionProvider;
        Options = options;

        Table = new SqliteTableAccessor<T>(this);
        Select = new SqliteSelectAccessor<T>(this, Table);
        Insert = new SqliteInsertAccessor<T>(this);
        Update = new SqliteUpdateAccessor<T>(this);
        Delete = new SqliteDeleteAccessor<T>(this, Table);

        InitializeCacheForInstance();
    }

    public SQLiteAsyncConnection Db => _connectionProvider.Connection;

    public ISqliteSelectAccessor<T> Select { get; }
    public ISqliteInsertAccessor<T> Insert { get; }
    public ISqliteUpdateAccessor<T> Update { get; }
    public ISqliteDeleteAccessor<T> Delete { get; }
    public ISqliteTableAccessor<T> Table { get; }

    public bool HasCache => HasDefinedCache;

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

    #region Fields And Properties

    private static readonly SqliteTableMetadata<T> Metadata = SqliteRepositoryMetadata<T>.Value;
    private static readonly ConcurrentDictionary<string, string> MergeSqlCache = new();
    private static readonly object CacheStateLock = new();
    private static readonly object CacheListenerLock = new();
    private static readonly SemaphoreSlim CacheRefreshLock = new(1, 1);
    private static readonly TimeSpan DefaultRefreshGap = TimeSpan.FromMilliseconds(300);

    // The PROVIDER, never a connection. A captured SQLiteAsyncConnection outlives a sign-out: a refresh
    // already in flight would finish against the previous user's database and publish those rows to the
    // next user. The connection is resolved from here at refresh time instead, and the provider is kept
    // across a session reset so the first read afterwards re-resolves rather than throwing.
    private static G9SqliteConnectionProvider? CacheProvider;
    private static List<T>? CacheRows;

    // Bumped by every session reset, under CacheStateLock. A refresh notes the value BEFORE it resolves
    // its connection and publishes only if it is unchanged afterwards — so rows that were loaded on one
    // side of a reset can never be published on the other.
    private static int CacheGeneration;

    // Held STRONGLY — ordinary event semantics. These were WeakReference<delegate>, which sounds safe and
    // is not: nothing else references the delegate object created by `ListenToCacheData(OnRows)` or by an
    // inline lambda, so it was collected at the next GC and the listener silently stopped being notified
    // while its owner was still alive. Unsubscribe with StopListeningToCacheData.
    private static readonly List<Action<List<T>>> CacheListeners = [];
    private static CancellationTokenSource? CacheDebounceCts;
    private static bool CacheDefined;
    private static bool CacheInitialized;
    private static bool EmptyCacheRetryAttempted;
    private static TimeSpan CacheRefreshGap = DefaultRefreshGap;

    private readonly G9SqliteConnectionProvider _connectionProvider;

    #endregion
}
