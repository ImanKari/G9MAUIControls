using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Everything the library was told at startup, frozen. Read by every later decision.
/// </summary>
public sealed class G9SqliteOptions
{
    internal G9SqliteOptions() { }

    /// <summary>Where the database file is. Defaults to a single file under app data.</summary>
    public IG9SqliteDatabaseLocator DatabaseLocator { get; internal set; } = new G9SingleFileDatabaseLocator();

    /// <summary>What "now" means for audit columns. Defaults to UTC.</summary>
    public IG9Clock Clock { get; internal set; } = new G9SystemClock();

    /// <summary>Who is writing. Defaults to nobody, which leaves audit user columns untouched.</summary>
    public IG9CurrentUserProvider CurrentUser { get; internal set; } = new G9NoCurrentUser();

    /// <summary>
    ///     Canonical casing for GUID strings the library writes. Defaults to UPPER case.
    ///     <para>
    ///         ⚠ Choose once, before first release. Rows already written keep the old casing (harmless in
    ///         SQL — every id column is <c>COLLATE NOCASE</c>), but if anything derives a <b>file path</b>
    ///         from a normalised id, changing this orphans the old directory on case-sensitive filesystems
    ///         and presents as total data loss. <see cref="SqliteGuidStringNormalizer.UseCanonicalCase" />
    ///         therefore refuses to change it once ids have been normalised.
    ///     </para>
    ///     <para>
    ///         <b>The default reads <c>Upper</c>, not <c>Lower</c>, deliberately.</b> This property was
    ///         declared with a <c>Lower</c> default but never applied — the normaliser was hard-coded to
    ///         upper case, so <c>Lower</c> described behaviour that did not exist and
    ///         <c>UseCanonicalIdCase</c> was a no-op. Now that it is honoured, the default states what the
    ///         library has always actually done, so honouring it cannot re-case an existing consumer's ids.
    ///     </para>
    /// </summary>
    public G9IdCase CanonicalIdCase { get; internal set; } = G9IdCase.Upper;

    /// <summary>
    ///     Retry budget for <c>SQLITE_BUSY</c>. Built in rather than left to each call site, which is where
    ///     the source app put it — every data service reimplementing the same back-off loop.
    /// </summary>
    public int BusyRetryAttempts { get; internal set; } = 4;

    /// <summary>First back-off delay; doubles per attempt.</summary>
    public int BusyRetryInitialDelayMs { get; internal set; } = 180;

    /// <summary>Migrations, ascending by <see cref="IG9SqliteMigration.Version" />.</summary>
    public IReadOnlyList<IG9SqliteMigration> Migrations { get; internal set; } = [];

    /// <summary>Initialisers, in registration order, run after migrations.</summary>
    public IReadOnlyList<IG9SqliteInitializer> Initializers { get; internal set; } = [];

    /// <summary>Interceptors, in registration order.</summary>
    public IReadOnlyList<IG9SqliteInterceptor> Interceptors { get; internal set; } = [];

    /// <summary>Frozen per-entity descriptors, keyed by entity type.</summary>
    public IReadOnlyDictionary<Type, G9EntityDescriptor> Entities { get; internal set; } =
        new Dictionary<Type, G9EntityDescriptor>();

    /// <summary>
    ///     The descriptor for <typeparamref name="T" />, creating a conventions-only one if the entity was
    ///     never explicitly configured — so an entity with no special rules needs no registration at all.
    /// </summary>
    public G9EntityDescriptor GetDescriptor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>()
        where T : new() =>
        Entities.TryGetValue(typeof(T), out var descriptor)
            ? descriptor
            : G9EntityConventions.Build(typeof(T));
}

/// <summary>
///     Configures the library. Every application-specific behaviour arrives through here.
///     <para>
///         <b>Why a builder rather than attributes alone.</b> Attributes are the right default — local,
///         discoverable, travelling with the entity. But they cannot express anything that varies by
///         deployment or that the entity's assembly must not know: a soft-delete column added by one app
///         sharing an entity library with another that has none, a converter needing a DI-resolved
///         serializer, an index suited to one app's query shape. The builder <b>overrides</b> attributes, so
///         the common case stays declarative and the awkward case stays possible.
///     </para>
/// </summary>
public sealed class G9SqliteBuilder
{
    private readonly G9SqliteOptions _options = new();
    private readonly List<IG9SqliteMigration> _migrations = [];
    private readonly List<IG9SqliteInitializer> _initializers = [];
    private readonly List<IG9SqliteInterceptor> _interceptors = [];
    private readonly Dictionary<Type, G9EntityDescriptor> _entities = [];

    /// <summary>Sets the database locator.</summary>
    public G9SqliteBuilder UseDatabaseLocator(IG9SqliteDatabaseLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _options.DatabaseLocator = locator;
        return this;
    }

    /// <summary>Sets the clock used for audit timestamps.</summary>
    public G9SqliteBuilder UseClock(IG9Clock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _options.Clock = clock;
        return this;
    }

    /// <summary>Sets the current-user provider used for audit user columns.</summary>
    public G9SqliteBuilder UseCurrentUserProvider(IG9CurrentUserProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _options.CurrentUser = provider;
        return this;
    }

    /// <summary>Sets the canonical GUID casing. See <see cref="G9SqliteOptions.CanonicalIdCase" /> first.</summary>
    public G9SqliteBuilder UseCanonicalIdCase(G9IdCase idCase)
    {
        _options.CanonicalIdCase = idCase;
        return this;
    }

    /// <summary>Tunes the <c>SQLITE_BUSY</c> retry budget.</summary>
    /// <param name="attempts">Total attempts including the first. Below 1 is clamped to 1.</param>
    /// <param name="initialDelayMs">First back-off delay; doubles per attempt.</param>
    public G9SqliteBuilder UseBusyRetry(int attempts, int initialDelayMs = 180)
    {
        _options.BusyRetryAttempts = Math.Max(1, attempts);
        _options.BusyRetryInitialDelayMs = Math.Max(0, initialDelayMs);
        return this;
    }

    /// <summary>
    ///     Registers a migration.
    ///     <para>
    ///         Explicit rather than assembly-scanned: scanning is reflection over types the trimmer then
    ///         cannot see are needed, which is hostile to this package's trimming posture — and it makes the
    ///         set of migrations depend on what happens to be in the assembly.
    ///     </para>
    /// </summary>
    public G9SqliteBuilder AddMigration(IG9SqliteMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        _migrations.Add(migration);
        return this;
    }

    /// <summary>Registers a migration by type, constructed with its parameterless constructor.</summary>
    public G9SqliteBuilder AddMigration<TMigration>() where TMigration : IG9SqliteMigration, new() =>
        AddMigration(new TMigration());

    /// <summary>Registers an initialiser, run once per resolved database path after migrations.</summary>
    public G9SqliteBuilder AddInitializer(IG9SqliteInitializer initializer)
    {
        ArgumentNullException.ThrowIfNull(initializer);
        _initializers.Add(initializer);
        return this;
    }

    /// <summary>
    ///     Registers an interceptor. Order matters when one stamps a field another reads — register the
    ///     general ones first.
    /// </summary>
    public G9SqliteBuilder AddInterceptor(IG9SqliteInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(interceptor);
        _interceptors.Add(interceptor);
        return this;
    }

    /// <summary>Registers an interceptor by type, constructed with its parameterless constructor.</summary>
    public G9SqliteBuilder AddInterceptor<TInterceptor>() where TInterceptor : IG9SqliteInterceptor, new() =>
        AddInterceptor(new TInterceptor());

    /// <summary>
    ///     Configures one entity. Conventions (a property named <c>Id</c>, <see cref="G9GuidIdAttribute" />,
    ///     <see cref="IG9AuditedEntity" />) are applied first, then <paramref name="configure" /> overrides
    ///     them — so this is additive, not a replacement.
    /// </summary>
    public G9SqliteBuilder Entity<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        Action<G9EntityBuilder<T>>? configure = null)
        where T : new()
    {
        var descriptor = G9EntityConventions.Build(typeof(T));
        if (configure is not null)
        {
            configure(new G9EntityBuilder<T>(descriptor));
        }

        _entities[typeof(T)] = descriptor;
        return this;
    }

    /// <summary>
    ///     Configures an entity through a dedicated class, for consumers who prefer one file per entity to
    ///     an inline lambda. Both shapes are supported because both are reasonable.
    /// </summary>
    public G9SqliteBuilder Entity<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T, TConfiguration>()
        where T : new()
        where TConfiguration : IG9EntityConfiguration<T>, new() =>
        Entity<T>(new TConfiguration().Configure);

    /// <summary>
    ///     Freezes the configuration.
    ///     <para>
    ///         Migrations are sorted by version here, and duplicate versions are <b>rejected</b> rather than
    ///         resolved arbitrarily: two migrations claiming the same version means one would silently never
    ///         run, which is a data-shape bug that surfaces much later.
    ///     </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">Two migrations share a version.</exception>
    public G9SqliteOptions Build()
    {
        var duplicate = _migrations
            .GroupBy(m => m.Version)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            var names = string.Join(", ", duplicate.Select(m => m.GetType().Name));
            throw new InvalidOperationException(
                $"Two or more migrations declare version {duplicate.Key} ({names}). Versions must be unique — " +
                "otherwise one of them would silently never run.");
        }

        _options.Migrations = [.. _migrations.OrderBy(m => m.Version)];
        _options.Initializers = [.. _initializers];
        _options.Interceptors = [.. _interceptors];
        _options.Entities = new Dictionary<Type, G9EntityDescriptor>(_entities);
        return _options;
    }
}

/// <summary>
///     Per-entity configuration as a class, the alternative to an inline lambda.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IG9EntityConfiguration<T> where T : new()
{
    /// <summary>Applies this entity's configuration.</summary>
    void Configure(G9EntityBuilder<T> entity);
}

/// <summary>Configures one entity's descriptor. Strongly typed, so property names come from expressions.</summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class G9EntityBuilder<T> where T : new()
{
    private readonly G9EntityDescriptor _descriptor;
    private readonly HashSet<string> _guidIds;
    private readonly List<G9IndexDescriptor> _indexes;

    internal G9EntityBuilder(G9EntityDescriptor descriptor)
    {
        _descriptor = descriptor;
        _guidIds = new HashSet<string>(descriptor.GuidIdProperties, StringComparer.Ordinal);
        _indexes = [.. descriptor.Indexes];
    }

    /// <summary>
    ///     Marks a string property as holding a GUID — normalised on write and in predicates, and declared
    ///     <c>COLLATE NOCASE</c>. The builder equivalent of <see cref="G9GuidIdAttribute" />, for when the
    ///     entity's assembly must not carry the attribute.
    /// </summary>
    public G9EntityBuilder<T> HasGuidId(Expression<Func<T, string?>> property)
    {
        _guidIds.Add(NameOf(property));
        Flush();
        return this;
    }

    /// <summary>
    ///     Makes deletes soft: a delete becomes an update of <paramref name="flag" />, and the write is
    ///     intercepted as <see cref="G9WriteKind.SoftDelete" />.
    ///     <para>
    ///         <b>Pair this with <see cref="AlwaysFilter" /></b> or soft-deleted rows keep appearing in
    ///         queries — which is the single most common way a soft-delete implementation goes wrong.
    ///     </para>
    /// </summary>
    public G9EntityBuilder<T> SoftDelete(Expression<Func<T, bool>> flag)
    {
        _descriptor.SoftDeleteProperty = NameOf(flag);
        return this;
    }

    /// <summary>
    ///     A predicate ANDed into every read of this entity. Usually <c>x =&gt; !x.IsDeleted</c>, or a
    ///     tenancy guard.
    /// </summary>
    public G9EntityBuilder<T> AlwaysFilter(Expression<Func<T, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _descriptor.AlwaysFilter = filter;
        return this;
    }

    /// <summary>Creates an index over one or more properties at initialisation.</summary>
    public G9EntityBuilder<T> Index(params Expression<Func<T, object?>>[] properties)
    {
        _indexes.Add(new G9IndexDescriptor([.. properties.Select(NameOfObject)], IsUnique: false));
        Flush();
        return this;
    }

    /// <summary>Creates a unique index over one or more properties.</summary>
    public G9EntityBuilder<T> Unique(params Expression<Func<T, object?>>[] properties)
    {
        _indexes.Add(new G9IndexDescriptor([.. properties.Select(NameOfObject)], IsUnique: true));
        Flush();
        return this;
    }

    /// <summary>Sets the default conflict policy for inserts on this entity. Overridable per call.</summary>
    public G9EntityBuilder<T> OnConflict(G9ConflictPolicy policy)
    {
        _descriptor.ConflictPolicy = policy;
        return this;
    }

    /// <summary>
    ///     Caches this entity's rows in memory. Only for small, hot, rarely-written tables — the cache holds
    ///     every row and a write invalidates all of it.
    /// </summary>
    public G9EntityBuilder<T> Cache(G9CachePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _descriptor.CachePolicy = policy;
        return this;
    }

    private void Flush()
    {
        _descriptor.GuidIdProperties = _guidIds;
        _descriptor.Indexes = _indexes;
    }

    private static string NameOf<TProp>(Expression<Func<T, TProp>> expression) =>
        expression.Body is MemberExpression member
            ? member.Member.Name
            : throw new ArgumentException(
                "Expected a simple property expression such as x => x.Name.", nameof(expression));

    private static string NameOfObject(Expression<Func<T, object?>> expression) =>
        expression.Body switch
        {
            MemberExpression member => member.Member.Name,
            // A value-typed property boxes into a Convert node, so unwrap it — otherwise Index(x => x.Order)
            // on an int would fail while the same call on a string succeeded, which is baffling.
            UnaryExpression { Operand: MemberExpression member } => member.Member.Name,
            _ => throw new ArgumentException(
                "Expected a simple property expression such as x => x.Name.", nameof(expression))
        };
}

/// <summary>
///     Derives what can be inferred about an entity without configuration, so an entity with no special
///     rules needs no registration.
/// </summary>
internal static class G9EntityConventions
{
    /// <summary>
    ///     Builds a descriptor from conventions: a property literally named <c>Id</c> plus anything marked
    ///     <see cref="G9GuidIdAttribute" /> are GUID ids, and <see cref="IG9AuditedEntity" /> means audited.
    /// </summary>
    /// <remarks>
    ///     <b>Only <c>Id</c> is recognised by name.</b> Property names ending in "Id" are not reliable —
    ///     <c>NationalId</c>, <c>EconomicalId</c>, <c>ExternalId</c> are business text, and normalising them
    ///     as GUIDs would corrupt them. Everything else must be marked explicitly.
    /// </remarks>
    public static G9EntityDescriptor Build(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type entityType)
    {
        var descriptor = new G9EntityDescriptor(entityType);
        var guidIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType != typeof(string))
            {
                continue;
            }

            if (string.Equals(property.Name, "Id", StringComparison.Ordinal) ||
                property.GetCustomAttribute<G9GuidIdAttribute>() is not null)
            {
                guidIds.Add(property.Name);
            }
        }

        descriptor.GuidIdProperties = guidIds;
        descriptor.IsAudited = typeof(IG9AuditedEntity).IsAssignableFrom(entityType);
        return descriptor;
    }
}
