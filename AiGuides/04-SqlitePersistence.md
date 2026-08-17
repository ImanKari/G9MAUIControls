# 04 - SQLite Persistence Package

# `G9MAUIControls.Persistence.Sqlite`
## Design, extension points, and what is deliberately not in it

---

# What this package owns

A fluent, cache-aware SQLite persistence layer over `sqlite-net-pcl`, extracted from the app
documented in `Common/Data/SqliteWrapperGuide.md` (~6,000 LOC). Concretely:

| Component | Role |
|---|---|
| `G9SqliteConnectionProvider` | owns the active `SQLiteAsyncConnection`, switches it when the active database changes, and is the ONLY thing that holds one |
| `G9SqliteRepository<T>` | the generic repository, exposed through four accessors: `Select`, `Insert`, `Update`, `Delete` |
| `G9SqliteQueryFactory` + builders | expression-tree → SQL for select / update / delete, including joins, projections, grouping and aggregates |
| `G9SqliteAggregate` | `COUNT` / `MIN` / `MAX` / `SUM` / `COALESCE` expressed in a projection so they run in SQL |
| `G9SqliteDtoCache<T>` / repository cache | debounced in-memory caches for small hot tables, invalidated by writes |
| `G9SqliteMigrationRunner` + `IG9SqliteMigration` | ordered, versioned schema migrations |
| `G9SqliteIdNormalizer` | canonical GUID-string form, applied everywhere the wrapper has mapped property context |

**What makes it worth packaging** is not "we wrapped SQLite". It is the accumulated correctness work:
expression-to-SQL translation that keeps filters in the database instead of in memory, GUID-id casing
handled consistently across a store whose rows can arrive in any case, and a cache-invalidation model
that keeps UI reads consistent with writes without blocking queries.

---

# The hard constraint that shapes everything: reflection

`sqlite-net-pcl` maps rows to objects by reflection, and this package adds its own reflection over
expression trees and entity metadata. The trimming guidance is direct about this class of library:

> *"If an API is mostly trim-incompatible, alternative coding approaches to the API might need to be
> considered. A common example is reflection-based serializers. In these cases, consider adopting other
> technology like source generators…"*

**Therefore this package does NOT get `IsAotCompatible=true`.** (Nor do `.Barcode` and `.IntroCarousel`,
for a different reason — an unannotated platform dependency rather than reflection of their own. Only the
core and `.ProgressOverlay` claim it; see the table in ADR-0011.) Claiming
it would be false, and a false claim is worse than an absent one: NativeAOT ignores the flag and trims
everything anyway, so the app breaks at runtime instead of warning at build time.

The posture instead:

1. `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` — surface the warnings without asserting compatibility.
2. Annotate the reflective surface honestly. Entity type parameters carry
   `[DynamicallyAccessedMembers(PublicProperties | PublicParameterlessConstructor)]` so the trimmer
   preserves what mapping needs. Where that cannot express the requirement, the API is marked
   `[RequiresUnreferencedCode]` and the annotation is **propagated to public API**, which is what stops
   consumers getting an unactionable `IL2104: assembly produced trim warnings`.
3. Ship a `TrimmerRootDescriptor` XML in the package so a consumer's entity assembly can be rooted
   without them working out the incantation.
4. Document the escape hatch: a consumer who needs NativeAOT should keep entity types in an assembly
   named in `TrimmerRootAssembly`.

**Recorded as ADR-0014.** A source-generated mapping layer is the real fix and is a v2 conversation, not
a v1 one — it would replace `sqlite-net-pcl`'s mapper entirely.

---

# The seven app couplings, and what each becomes

The extraction audit found the source's coupling is narrow — nine `TimeKeeperHelper` call sites, six
`IAuditedEntity`, one database-path service. Each becomes an explicit extension point.

| Source coupling | Becomes | Kind |
|---|---|---|
| `TimeKeeperHelper.NowTehranWallClock()` | `IG9Clock` | ambient value provider |
| `ResolveCurrentUserId()` (cached static reaching into auth) | `IG9CurrentUserProvider` | ambient value provider |
| `UserDataPartitionService` → db file path | `IG9SqliteDatabaseLocator` | strategy |
| `IAuditedEntity`, `AuditedEntity`, `BusinessUnitEntity` (`Buid`) | `IG9AuditedEntity` kept; `Buid` dropped | contract |
| `[SqliteGuidIdColumn]` attribute | kept, plus a builder override | descriptor |
| `MigrationsFlow/Migration_1_0_0_*` | consumer-registered `IG9SqliteMigration` list | registration |
| Dotmim sync awareness | **removed entirely** — see "Not in this package" | — |

## Why ambient providers are interfaces and not constructor values

The clock and the current user are read **at write time**, not at registration time. A login changes the
current user while the same repository instances live on; a captured `string userId` would silently
stamp every later row with the user who happened to be signed in when DI built the graph. That is the
class of bug that shows up as "audit columns are wrong for the second user on a shared device", weeks
later, in data nobody re-reads.

```csharp
public interface IG9Clock
{
    // The value written to CreatedTime / UpdatedTime. Return whatever the app considers "now":
    // UTC, device local, or a fixed business timezone. The library never assumes.
    DateTime Now();
}

public interface IG9CurrentUserProvider
{
    // Null is legitimate and handled: audit user columns keep whatever the entity already carried
    // rather than being overwritten with nothing.
    string? GetCurrentUserId();
}
```

Defaults are supplied so an app that does not care writes nothing: `G9SystemClock` (UTC) and
`G9NoCurrentUser`.

---

# The design: configuration builder + entity descriptors + interceptors

The user's brief listed candidate patterns (interfaces, generic repositories, strategies,
interceptors/hooks, configuration builders, DI registration, partial implementations, entity mapping
descriptors). They are not alternatives — each answers a *different* question, and the design uses four
of them at the layer each fits. What follows says which, and why the others were rejected there.

## Layer 1 — a configuration builder, for everything decided once at startup

```csharp
services.AddG9Sqlite(sqlite =>
{
    sqlite.UseDatabaseLocator<PerUserDatabaseLocator>()
          .UseClock<TehranWallClock>()
          .UseCurrentUserProvider<SignedInUserProvider>()
          .UseCanonicalIdCase(G9IdCase.Lower)

          .AddMigration<Migration_1_0_0_1>()
          .AddMigration<Migration_1_0_0_2>()

          .Entity<SampleEntity>(e =>
          {
              e.HasGuidId(x => x.SamplingId)
               .HasGuidId(x => x.VarietyId)
               .SoftDelete(x => x.IsDeleted)
               .AlwaysFilter(x => !x.IsDeleted)
               .Index(x => x.SamplingId)
               .Convert(x => x.Metadata, new JsonConverter<SampleMetadata>())
               .Cache(G9CachePolicy.Debounced(250));
          })

          .AddInterceptor<SyncMetadataInterceptor>();
});
```

**Why a builder rather than attributes alone.** Attributes are the right *default* — `[G9GuidId]` on a
property is local, discoverable, and travels with the entity. But attributes cannot express anything
that varies by deployment or that the entity's assembly must not know: a soft-delete column added by an
app that shares an entity library with an app that has none, a converter that needs a DI-resolved
serializer, an index appropriate to one app's query shape. The builder **overrides** attributes rather
than replacing them, so the common case stays declarative and the awkward case stays possible.

**Why not an `EntityTypeConfiguration<T>` class per entity (the EF Core `IEntityTypeConfiguration`
shape).** It was considered and rejected for v1: it requires either assembly scanning (reflection —
hostile to the trimming posture above) or one explicit registration line per entity, which is the same
line count as the inline lambda with an extra file each. The builder accepts an
`IG9EntityConfiguration<T>` overload for consumers who prefer the per-class shape, so both work.

## Layer 2 — entity descriptors, the immutable result of layer 1

The builder produces one frozen `G9EntityDescriptor` per entity, and **every** later decision reads it:
which property is the id, which are GUID-normalised, which is the soft-delete flag, the always-filter,
converters, cache policy, audit participation.

This matters for a reason beyond tidiness: it is the single place that answers "what does the library
know about this type", so the reflection happens **once at startup** rather than per query. That is both
the performance answer and the trimming answer — one annotated entry point instead of reflection
scattered through the write path.

## Layer 3 — interceptors, for per-operation behaviour

```csharp
public interface IG9SqliteInterceptor
{
    // Runs before the write is translated to SQL. Mutate the entity, or veto with a reason.
    ValueTask<G9WriteDecision> OnWritingAsync(G9WriteContext context, CancellationToken ct);

    // Runs after the write committed. The place for sync bookkeeping, outbox rows, cache signals.
    ValueTask OnWrittenAsync(G9WriteContext context, CancellationToken ct);

    // Contributes an extra predicate ANDed into every delete/update for this entity.
    Expression<Func<T, bool>>? AdditionalCondition<T>(G9WriteKind kind);
}
```

**Why interceptors, and not repository inheritance.** The brief asked about "entity-specific
insert/update/delete rules" and "per-entity hooks". Inheritance answers that by making the consumer
subclass a repository per entity — fifty subclasses to add one rule to three of them, and no way to
apply a cross-cutting rule (say, "stamp a sync-dirty flag on every write") without touching all fifty. A
pipeline of interceptors, each declaring which entities it applies to, expresses both the per-entity
rule and the cross-cutting one, and composes when an app needs both.

**Why a `G9WriteDecision` return rather than `void` or an exception.** A veto is an expected outcome, not
a failure. "This row is read-only for this user" or "this delete is a no-op because a newer version
exists" are business answers the caller must be able to handle; making them exceptions turns ordinary
control flow into stack unwinding and loses the reason.

## Layer 4 — strategies, for the two things with genuinely different algorithms

**`IG9SqliteDatabaseLocator`** — where the database file is. The source's per-user partition scheme is one
implementation; single-file, per-tenant and in-memory (tests) are others. It is a strategy because the
implementations share no code, only a question.

```csharp
public interface IG9SqliteDatabaseLocator
{
    // Called on every connection acquisition. Returning a different path swaps the connection, which
    // is what makes user switching work without anything caching a stale SQLiteAsyncConnection.
    string GetDatabasePath();

    // Raised when the answer changes. The provider closes the old connection and resets caches.
    event EventHandler? DatabasePathChanged;
}
```

**`IG9ConflictPolicy`** — what `INSERT` does on a key collision (`ABORT` / `IGNORE` / `REPLACE` / a
merge). Per-entity default from the descriptor, overridable per call.

---

# The brief's checklist, answered

| Requirement | Where it lives |
|---|---|
| entity-specific insert/update/delete rules | `IG9SqliteInterceptor.OnWritingAsync` + descriptor |
| additional update/delete conditions | `IG9SqliteInterceptor.AdditionalCondition` |
| soft-delete behaviour | `descriptor.SoftDelete(x => x.IsDeleted)` + `AlwaysFilter`; `Delete` writes an update |
| application-owned fields | descriptor `Convert` / interceptor `OnWritingAsync` |
| synchronization metadata | an interceptor — the library ships none (see below) |
| migrations | ordered `IG9SqliteMigration`, explicitly registered |
| custom SQL | `repository.Sql` accessor, with the id-normalisation caveat documented |
| transaction boundaries | `IG9SqliteTransaction` scope, `RunInTransactionAsync` |
| conflict handling | `IG9ConflictPolicy`, per-entity default + per-call override |
| serialization / conversion | `IG9ValueConverter<TModel, TStore>` per property |
| per-entity hooks | interceptors scoped by entity type |
| database initialization | `IG9SqliteInitializer`, run once per resolved database path |
| indexes and constraints | descriptor `Index(...)` / `Unique(...)`, emitted by the initializer |

---

# Not in this package, deliberately

**Dotmim.Sync awareness.** The source guide is full of it — apply-phase writes bypassing normalisation,
`scope_info_client` retry rows, the rule that migrations must never touch sync triggers. All of that is
*correct for that app* and belongs to whoever chose that sync engine. A persistence package that knew
about a specific sync framework would be unusable by anyone who picked a different one, and the
interceptor pipeline is exactly the seam a sync integration hangs off. If a G9 sync integration is
wanted later it is `G9MAUIControls.Persistence.Sqlite.Sync`, a satellite of a satellite, and its own ADR.

**The `Buid` business-unit column.** `BusinessUnitEntity` is a domain concept. Multi-tenancy is
expressible as a descriptor filter plus an interceptor that stamps the column, which is the generic form
of the same idea.

**The uppercase-vs-lowercase canonical GUID decision.** `UseCanonicalIdCase` is configurable with a
**lowercase default** (matching `Guid.ToString()`), and the guide states plainly: choose once, before
first release, and never change it on a shipped app.

> **Status (1.0.2).** Two corrections landed here. (1) The setting was declared but never READ — the
> normaliser was hard-coded to upper — and when it was wired up the default was briefly set to `Upper`
> for bug-compatibility. It is now `Lower`, as designed above. See ADR-0018 and LES-0038. (2) The
> reason given below for why the source "cannot flip it" has been SOLVED, not merely accepted: the
> consumer adopts the differently-cased partition directory by RENAMING it on first activation
> (`UserDataPartitionService.TryAdoptDifferentlyCasedDirectory`), which is atomic and needs no free
> space. Verified on device with a 722 MB database: zero rows lost. The hazard is real, but it is
> migratable, and any consumer deriving a path from a normalised id must do the same.

**A "database is locked" retry helper as app-level advice.** The source guide tells each DataService to
write its own back-off loop. That is a library responsibility, not a consumer one: retry-on-`SQLITE_BUSY`
with bounded exponential back-off is built into the write path and configurable via
`sqlite.UseBusyRetry(...)`.

---

# The rule the library cannot enforce, and must therefore shout about

The source guide's longest and most expensive section is in-memory GUID id comparison: rows can arrive
with any casing, every id column is `COLLATE NOCASE` so SQL is safe, and a plain C# `==` on a
materialised list is ordinal and silently returns "no match". It cost that app a vanished map zoom and a
whole screen of missing avatars.

**A library cannot fix this.** Once rows are objects, comparison is the consumer's code. What this
package does instead:

1. `COLLATE NOCASE` on every GUID-id column it creates, so anything expressed as a query is correct.
2. `G9IdComparer.Ordinal` — a ready `StringComparer` to pass to every id-keyed dictionary, set, and
   `GroupBy`, so the correct thing is the shortest thing to type.
3. An analyzer is the real answer (flagging `==` between two properties the descriptor marks as GUID ids)
   and is recorded as future work, not shipped.
4. The package README leads with this, because a consumer who reads nothing else must read this.

---

# Open questions to settle before implementation

1. **`sqlite-net-pcl` or `Microsoft.Data.Sqlite`?** The source uses `sqlite-net-pcl`, and the current MAUI
   local-database guidance still names it for MAUI projects while noting `Microsoft.Data.Sqlite` as the
   lower-level, EF-Core-adjacent alternative. `sqlite-net-pcl` is chosen for v1 because the extracted
   code is built on its `SQLiteAsyncConnection` and its mapper — swapping to ADO.NET is a rewrite of the
   materialisation layer, not a dependency change. Both embed SQLite via `SQLitePCLRaw.bundle_green`,
   which is the part that matters for iOS AOT. Recorded as ADR-0015.
2. **Does the package own the per-user partition scheme, or only the locator interface?** Proposal: only
   the interface, plus a `G9PerUserDatabaseLocator` in the box as a working reference — the scheme is
   good but the directory naming is an app decision (and the source's uppercase-folder trap proves it).
3. **Migration numbering.** The source uses `Migration_1_0_0_N` classes with gaps (5 and 6 absent). The
   package should require an explicit `long Version` on the interface rather than deriving it from the
   type name, so gaps and renames are harmless.
