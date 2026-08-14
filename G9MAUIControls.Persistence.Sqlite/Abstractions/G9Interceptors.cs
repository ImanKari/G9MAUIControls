using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>Which write is happening. Interceptors branch on it.</summary>
public enum G9WriteKind
{
    /// <summary>A new row.</summary>
    Insert,

    /// <summary>An existing row, whole-entity.</summary>
    Update,

    /// <summary>A partial update expressed as SET clauses, with no entity instance.</summary>
    PartialUpdate,

    /// <summary>A hard delete.</summary>
    Delete,

    /// <summary>
    ///     A soft delete — reaches the database as an update, but is intercepted as a delete, because that
    ///     is what the caller meant and what a rule about deletion should see.
    /// </summary>
    SoftDelete,

    /// <summary>Insert-or-update, resolved per row.</summary>
    Upsert
}

/// <summary>What an interceptor is told about a write.</summary>
public sealed class G9WriteContext
{
    internal G9WriteContext(G9WriteKind kind, Type entityType, object? entity, int rowCount)
    {
        Kind = kind;
        EntityType = entityType;
        Entity = entity;
        RowCount = rowCount;
    }

    /// <summary>The operation.</summary>
    public G9WriteKind Kind { get; }

    /// <summary>The entity type, always available — including for a partial update with no instance.</summary>
    public Type EntityType { get; }

    /// <summary>
    ///     The entity instance, or <c>null</c> for a set-based operation
    ///     (<see cref="G9WriteKind.PartialUpdate" />, a predicate delete). Mutate it in
    ///     <see cref="IG9SqliteInterceptor.OnWritingAsync" /> to stamp fields.
    /// </summary>
    public object? Entity { get; }

    /// <summary>
    ///     Rows in this operation — 1 for a single write, N for a batch, -1 when a set-based operation's
    ///     count is not known before execution.
    /// </summary>
    public int RowCount { get; }

    /// <summary>
    ///     Free-form state shared between <see cref="IG9SqliteInterceptor.OnWritingAsync" /> and
    ///     <see cref="IG9SqliteInterceptor.OnWrittenAsync" /> for the same operation. Use it to carry a
    ///     before-image, a correlation id, a stopwatch — anything the "after" needs from the "before".
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The typed entity, or <c>null</c> when absent or of another type.</summary>
    public T? EntityAs<T>() where T : class => Entity as T;
}

/// <summary>Whether a write proceeds, and why not if it does not.</summary>
public readonly record struct G9WriteDecision
{
    private G9WriteDecision(bool proceed, string? reason)
    {
        Proceed = proceed;
        Reason = reason;
    }

    /// <summary>False when an interceptor vetoed.</summary>
    public bool Proceed { get; }

    /// <summary>Why it was vetoed. Surfaced to the caller; never <c>null</c> on a veto.</summary>
    public string? Reason { get; }

    /// <summary>Allow it.</summary>
    public static G9WriteDecision Allow() => new(true, null);

    /// <summary>
    ///     Veto it.
    ///     <para>
    ///         <b>A veto is an expected outcome, not a failure.</b> "read-only for this user", "a newer
    ///         version exists" are business answers the caller must be able to handle — which is why this
    ///         returns rather than throwing. An exception would turn ordinary control flow into stack
    ///         unwinding and lose the reason.
    ///     </para>
    /// </summary>
    public static G9WriteDecision Veto(string reason) => new(false, reason);
}

/// <summary>
///     Hooks into writes: per-entity rules, cross-cutting stamping, extra conditions, post-write bookkeeping.
///     <para>
///         <b>Why interceptors rather than repository inheritance.</b> Inheritance would mean a subclass per
///         entity — fifty subclasses to add one rule to three of them, and no way at all to express a
///         cross-cutting rule ("stamp a sync-dirty flag on every write") without touching all fifty. A
///         pipeline expresses both, and composes when an app needs both at once.
///     </para>
///     <para>
///         Interceptors run in registration order. Ordering matters when one stamps a field another reads,
///         so register the general ones before the specific ones.
///     </para>
/// </summary>
public interface IG9SqliteInterceptor
{
    /// <summary>
    ///     Whether this interceptor applies to <paramref name="entityType" />. Called once per type and
    ///     cached, so it must be a pure function of the type — not of runtime state.
    ///     <para>Return true for everything to make a cross-cutting interceptor.</para>
    /// </summary>
    bool AppliesTo(Type entityType);

    /// <summary>
    ///     Before the write is translated to SQL. Mutate <see cref="G9WriteContext.Entity" />, or veto.
    ///     <para>
    ///         Runs inside the transaction, so a throw rolls the write back. Keep it fast: it is on the write
    ///         path for every row.
    ///     </para>
    /// </summary>
    ValueTask<G9WriteDecision> OnWritingAsync(G9WriteContext context, CancellationToken cancellationToken);

    /// <summary>
    ///     After the write committed. The place for outbox rows, sync bookkeeping, cache signals.
    ///     <para>
    ///         <b>A throw here cannot undo the write</b> — it is already committed. Handle your own failures;
    ///         the library logs and continues rather than reporting a write that succeeded as failed.
    ///     </para>
    /// </summary>
    ValueTask OnWrittenAsync(G9WriteContext context, CancellationToken cancellationToken);
}

/// <summary>
///     Base class implementing every member as a no-op, so an interceptor overrides only what it needs.
///     <para>
///         Prefer this to implementing the interface directly: a member added to
///         <see cref="IG9SqliteInterceptor" /> in a later version is a breaking change for a direct
///         implementer and a non-event for a subclass.
///     </para>
/// </summary>
public abstract class G9SqliteInterceptor : IG9SqliteInterceptor
{
    /// <inheritdoc />
    /// <remarks>Defaults to every entity type.</remarks>
    public virtual bool AppliesTo(Type entityType) => true;

    /// <inheritdoc />
    public virtual ValueTask<G9WriteDecision> OnWritingAsync(G9WriteContext context, CancellationToken cancellationToken) =>
        new(G9WriteDecision.Allow());

    /// <inheritdoc />
    public virtual ValueTask OnWrittenAsync(G9WriteContext context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

/// <summary>
///     Contributes an extra predicate ANDed into every update and delete for one entity type.
///     <para>
///         Separate from <see cref="IG9SqliteInterceptor" /> because it is generic in the entity type, which
///         an interface method cannot be while staying implementable per type. Implement it alongside an
///         interceptor when a rule must reach the SQL rather than inspect an instance — a tenancy guard, an
///         optimistic-concurrency check, "never touch a locked row".
///     </para>
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IG9WriteConditionProvider<T> where T : new()
{
    /// <summary>
    ///     The extra condition, or <c>null</c> for none. ANDed with the caller's own predicate, so it can
    ///     only ever narrow what is affected — never widen it.
    /// </summary>
    Expression<Func<T, bool>>? GetCondition(G9WriteKind kind);
}

/// <summary>What <c>INSERT</c> does when a key collides.</summary>
public enum G9ConflictPolicy
{
    /// <summary>Fail. The default — a surprise collision is usually a bug worth hearing about.</summary>
    Abort,

    /// <summary>Skip the row silently.</summary>
    Ignore,

    /// <summary>
    ///     Replace the row wholesale.
    ///     <para>
    ///         ⚠ In SQLite <c>INSERT OR REPLACE</c> is a DELETE followed by an INSERT. It fires delete
    ///         triggers and, more surprisingly, resets any column the new row does not supply back to its
    ///         default. Prefer <see cref="Upsert" /> unless you genuinely mean "discard the old row".
    ///     </para>
    /// </summary>
    Replace,

    /// <summary>
    ///     <c>ON CONFLICT … DO UPDATE SET</c> — updates only the columns supplied, leaving the rest intact.
    ///     Usually what "merge" is meant to mean.
    /// </summary>
    Upsert
}

/// <summary>
///     An explicit transaction spanning several repositories.
///     <para>
///         Dispose without <see cref="CommitAsync" /> and it rolls back, so a <c>using</c> block is
///         fail-safe: an early return or a throw cannot leave a half-applied set of writes.
///     </para>
/// </summary>
public interface IG9SqliteTransaction : IAsyncDisposable
{
    /// <summary>Commits. Calling twice is an error; not calling it at all rolls back.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls back explicitly. Disposal does this anyway if nothing was committed.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Everything the library knows about one entity type, frozen at startup.
///     <para>
///         Built once by the configuration builder and read by every later decision — write normalisation,
///         predicate binding, soft delete, query filters, cache policy. Building it once is both the
///         performance answer (reflection at startup, not per query) and the trimming answer (one annotated
///         entry point instead of reflection scattered through the write path).
///     </para>
/// </summary>
public sealed class G9EntityDescriptor
{
    internal G9EntityDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type entityType)
    {
        EntityType = entityType;
    }

    /// <summary>The entity type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type EntityType { get; }

    /// <summary>
    ///     Property names holding GUID strings — normalised on write and in predicates, and declared
    ///     <c>COLLATE NOCASE</c>. Includes a property literally named <c>Id</c> automatically, plus anything
    ///     marked <see cref="G9GuidIdAttribute" /> or added through the builder.
    /// </summary>
    public IReadOnlySet<string> GuidIdProperties { get; internal set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    ///     The soft-delete flag property, or <c>null</c> for hard deletes. When set, a delete becomes an
    ///     update of this column and the write is intercepted as <see cref="G9WriteKind.SoftDelete" />.
    /// </summary>
    public string? SoftDeleteProperty { get; internal set; }

    /// <summary>
    ///     A predicate ANDed into every read. The natural companion to soft delete
    ///     (<c>x =&gt; !x.IsDeleted</c>), so deleted rows disappear from queries without every call site
    ///     remembering.
    /// </summary>
    public LambdaExpression? AlwaysFilter { get; internal set; }

    /// <summary>Whether the library maintains audit columns — true when the type is an <see cref="IG9AuditedEntity" />.</summary>
    public bool IsAudited { get; internal set; }

    /// <summary>Default conflict policy for inserts on this entity. Overridable per call.</summary>
    public G9ConflictPolicy ConflictPolicy { get; internal set; } = G9ConflictPolicy.Abort;

    /// <summary>Cache policy, or <c>null</c> for no cache.</summary>
    public G9CachePolicy? CachePolicy { get; internal set; }

    /// <summary>Indexes to create at initialisation. Each entry is one or more property names.</summary>
    public IReadOnlyList<G9IndexDescriptor> Indexes { get; internal set; } = [];
}

/// <summary>One index.</summary>
/// <param name="Properties">Property names, in index order.</param>
/// <param name="IsUnique">Whether it is a unique index.</param>
public readonly record struct G9IndexDescriptor(IReadOnlyList<string> Properties, bool IsUnique);

/// <summary>
///     Whether and how an entity's rows are cached in memory.
///     <para>
///         For small, hot, rarely-written reference tables. <b>Not</b> for anything large or
///         frequently-written: the cache holds every row of the table, and a write invalidates the whole
///         thing.
///     </para>
/// </summary>
public sealed class G9CachePolicy
{
    private G9CachePolicy(int debounceMs) => DebounceMs = debounceMs;

    /// <summary>How long to coalesce invalidations. 0 refreshes immediately on every write.</summary>
    public int DebounceMs { get; }

    /// <summary>Refresh immediately on each write. Only for a table written rarely and read constantly.</summary>
    public static G9CachePolicy Immediate() => new(0);

    /// <summary>
    ///     Coalesce invalidations over a window. The right choice for a table a batch import touches: one
    ///     refresh at the end instead of one per row.
    /// </summary>
    /// <param name="debounceMs">Window in milliseconds. 250 is a good default.</param>
    public static G9CachePolicy Debounced(int debounceMs = 250) => new(Math.Max(0, debounceMs));
}
