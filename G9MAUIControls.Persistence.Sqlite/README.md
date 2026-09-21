# G9MAUIControls.Persistence.Sqlite

A fluent, cache-aware SQLite persistence layer for .NET MAUI apps.

```
dotnet add package G9MAUIControls.Persistence.Sqlite
```

> Part of the `G9MAUIControls` family, but it does **not** depend on the controls. Install it on its own.

---

## Read this first: GUID ids and in-memory comparison

If you store GUIDs as strings — which this package does — **every in-memory comparison must be
case-insensitive**, and the library cannot enforce that for you.

Rows can arrive in any case: a sync engine's apply phase writes server casing verbatim, and a second store
(a geodatabase, an import) has its own. **Declare every id column `COLLATE NOCASE` in your schema** — this
package does not create your tables or columns, so it cannot do that for you — and anything expressed as a
**query** is then correct. But once rows are objects, `==`, `FirstOrDefault(x => x.Id == id)`,
`Dictionary<string, …>` and `HashSet<string>` are **ordinal**, and silently return "no match".

```csharp
// GOOD
var byId = items.ToDictionary(i => i.Id, G9IdComparer.Ordinal);
var hit  = items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

// BAD — ordinal; breaks the moment casing differs
var byId = items.ToDictionary(i => i.Id);
var hit  = items.FirstOrDefault(i => i.Id == id);
```

Better still: **push the filter into the query.** Never load-all-then-filter-by-id.

**Mint and normalise ids through the package**, so app-built ids and repository-built ids cannot diverge:

```csharp
using G9MAUIControls.Persistence.Sqlite;

var id       = SqliteEntityAuditDefaults.CreateNewId();   // canonical: lower-case GUID, no braces
var incoming = rawId.NormalizeSqliteGuidId();             // extension, on string / Guid / Guid?
```

`SqliteGuidStringNormalizer` carries those extensions. It is public **because it is part of the storage
contract, not an implementation detail** — a consumer that normalises differently gets no error, it just
stops finding rows.

This is first in the README because it is the defect this design has actually paid for: a case-sensitive
in-memory cache once made an entire screen of data silently vanish while every SQL query matched fine.

---

## What it gives you

- **A generic repository** with four accessors — `Select`, `Insert`, `Update`, `Delete`.
- **Expression-to-SQL** — projections, inner/left joins, grouping, and
  `COUNT`/`MIN`/`MAX`/`SUM`/`COALESCE` — so filtering and aggregation happen in the database rather than
  after materialising a table.
- **Partial updates without loading the entity**: `Update.ExecuteAsync(q => q.Where(…).Set(…))`.
- **Batch insert and merge** with `ON CONFLICT`, inside one transaction.
- **Debounced caches** for small hot reference tables, invalidated by writes so UI reads stay consistent
  with the database without blocking queries.
- **Ordered migrations** through `SqliteMigrationRunner` (`Register<TMigration>(version)`, then
  `RunAsync(provider, logger)`).
- **Connection tuning that survives a reconnect**: `ApplyPerformancePragmasAsync()` opts in once (WAL,
  `synchronous=NORMAL`, a 5 s `busy_timeout`, …) and the provider re-applies it to every connection it opens
  afterwards. There is **no** built-in `SQLITE_BUSY` retry loop — see "Reserved" below.

```csharp
var rows = await sampleRepo.Select.QueryAsync(q => q
    .Select(s => new { s.Id, s.Code, s.UpdatedTime })
    .Where(s => s.SamplingId == samplingId)
    .OrderByDescending(s => s.UpdatedTime)
    .Limit(50));

await sampleRepo.Update.ExecuteAsync(q => q
    .Where(s => s.Id == id)
    .Set(s => s.BatchId, batchId));
```

---

## Your domain stays yours

The package owns the generic infrastructure. Everything application-specific is injected:

```csharp
services.AddG9Sqlite(sqlite =>
{
    // The three ambient services take INSTANCES, not type arguments — they almost always need
    // constructor dependencies of your own (the signed-in user, your partition service, your clock).
    sqlite.UseDatabaseLocator(new PerUserDatabaseLocator(partitions))  // where the file lives
          .UseClock(new AppClock())                                   // what "now" means
          .UseCurrentUserProvider(new SignedInUser(auth))             // audit columns
          .UseCanonicalIdCase(G9IdCase.Lower);                        // choose once, before first release
});
```

Those four settings are what the builder does **today**. The package knows nothing about your entities or
your business rules — and deliberately knows nothing about any particular sync framework.

### Reserved — accepted by the builder, currently with NO effect

The rest of the builder surface is the designed shape of a later version. It compiles, it is stored on
`G9SqliteOptions`, and **nothing reads it yet**. Do not build on it:

| Builder member | What actually happens today | Use instead |
|---|---|---|
| `AddMigration` | stored, never run | `SqliteMigrationRunner.Register<T>(version)` + `RunAsync` |
| `AddInterceptor`, `AddInitializer` | stored, never called | your own code around the write |
| `Entity<T>().SoftDelete(…)` | every delete is a **hard** delete | `Update.ExecuteAsync(q => q.Set(x => x.IsDeleted, true).Where(…))` |
| `Entity<T>().AlwaysFilter(…)` | **no query is filtered** | put the predicate in each `Where` |
| `Entity<T>().HasGuidId(…)`, `[G9GuidId]` | not read | `[SqliteGuidIdColumn]` on the property (a property named `Id` is automatic) |
| `Entity<T>().Index(…)` / `Unique(…)` | no index is created, no uniqueness enforced | a migration, or sqlite-net's `[Indexed]` |
| `Entity<T>().OnConflict(…)` | not read | the method chooses: `Insert.OneAsync` fails, `OrReplaceAsync` replaces, `MergeAsync` upserts |
| `Entity<T>().Cache(…)` | no cache is created | `SqliteRepository<T>.DefineCache(provider)` / `SqliteDtoCache<,>.DefineCache(…)` |
| `UseBusyRetry` | no retry loop exists | the 5 s `busy_timeout` from `ApplyPerformancePragmasAsync()`; your own retry if you need more |

Each of these members says the same in its XML documentation.

The clock and the current user are **interfaces read at write time**, not values captured at
registration. A captured user id would stamp every later row with whoever was signed in when DI built the
graph — which surfaces weeks later, in audit data nobody re-reads.

---

## Trimming and NativeAOT — an honest note

`sqlite-net` maps rows by reflection, and this layer adds expression-tree reflection on top. **This
package does not claim `IsAotCompatible`**, because the claim would be false — and a false claim is worse
than none: NativeAOT trims regardless of the flag, so you would get a runtime failure instead of a build
warning. `EnableTrimAnalyzer` is on, so the warnings stay visible.

If you publish with trimming, you need **both** of these in your own project:

```xml
<ItemGroup Condition="'$(Configuration)' == 'Release'">
    <!-- Keeps your entity properties from being trimmed away. -->
    <TrimmerRootAssembly Include="YourApp" />
</ItemGroup>
<PropertyGroup>
    <!-- This package relaxes these codes for its own build; that setting does NOT travel to you. -->
    <WarningsNotAsErrors>$(WarningsNotAsErrors);IL2026;IL2070;IL2077;IL2087;IL2091;IL2111</WarningsNotAsErrors>
</PropertyGroup>
```

The second one surprises people. `WarningsNotAsErrors` is per-project, so when *you* publish with
`PublishTrimmed`, the trimmer re-analyses this package's IL and reports its reflection sites against your
project — where they are errors and fail the publish with `NETSDK1144`. A `[SuppressMessage]` in your code
cannot reach them, because they originate inside this assembly. Everything builds green right up to
`publish`.

No `TrimmerRootDescriptor` ships in the package, and one could not do this for you: what has to be rooted
is **your** entity assembly, whose name a library cannot know. The two settings above are the whole recipe.

---

## Session boundaries (sign-out, user switch)

```csharp
await provider.SwitchDatabaseAsync();   // closes the connection and resets every cache, without blocking
partitions.Deactivate();                // THEN let the locator change its answer
```

`IG9SqliteDatabaseLocator.DatabasePathChanged` still closes the connection and resets the caches on its
own, but it is a synchronous event, so it has to block the raising thread — usually the UI thread — while
SQLite closes. Awaiting `SwitchDatabaseAsync()` first leaves that handler nothing to wait for.

Cache listeners (`ListenToCacheData`) are held **strongly**, like event handlers: unsubscribe with
`StopListeningToCacheData` when the subscriber goes away.

---

## Changes that can alter what existing code does

**Read this list before upgrading.** Everything else in this release is additive or a pure fix.

- **`!=` now returns rows whose column is NULL.** `Where(x => x.Status != value)` is emitted as
  `([Status] != ? OR [Status] IS NULL)`. That is what the C# predicate says — `null != value` is `true` —
  but it was previously emitted as `[Status] != ?`, which SQL evaluates to NULL for a NULL column and so
  **dropped** those rows. Queries that relied on that, knowingly or not, now return MORE rows, and an
  `Update`/`Delete` using such a predicate now affects more rows. A query that already says
  `x.Col != null && x.Col != value` is unaffected. If you want the old result, say so:
  `x.Col != null && x.Col != value`.
- **A captured `null` now compares as NULL.** `Where(x => x.ParentId == parentId)` with `parentId == null`
  is emitted as `[ParentId] IS NULL` (and `!=` as `IS NOT NULL`). It was `[ParentId] = NULL`, which matched
  nothing — so such a query now returns rows where it used to return none.
- **`Update` / `Delete` builders refuse to build without a `WHERE`.** They throw
  `InvalidOperationException` instead of emitting a statement that hits every row. Call the new `.AllRows()`
  when the whole table really is the target. (`Delete.AllAsync()` is unchanged.)
- **`Insert.MergeAsync`** no longer overwrites `CreatedTime` / `CreatedByUserId` of an existing row on an
  `IG9AuditedEntity`, and throws `ArgumentException` when a library-managed GUID primary key is the empty GUID
  (every such row used to be upserted onto the same row).
- **`Insert.BatchAsync(…, runInSingleTransaction: false)`** now means one transaction **per batch**; it used
  to mean no transaction at all, so a failure mid-batch left part of that batch committed.
- **Partial updates no longer write `UpdatedByUserId = NULL`** when nobody is signed in; the column is left
  as it was. `UpdatedTime` is still stamped.
- **A failed cache refresh is retried by the next read** instead of the cache serving pre-write rows
  indefinitely — so a read can now surface a database error that used to be swallowed.
- **Cache reads after a session reset re-resolve the connection** instead of throwing until the next write.
- Generated SQL text differs where the result does not: table names are quoted (`FROM [Sample]`),
  arithmetic is parenthesised (`(([A] + [B]) * [C])`), `Offset` without `Limit` emits `LIMIT -1 OFFSET n`
  (it was a syntax error), `AnyAsync` runs `SELECT EXISTS(…)`, and an `IN` list above 16,000 values is sent
  as one JSON parameter (`IN (SELECT value FROM json_each(?))`) — above 32,766 it used to fail outright.
  C# 14's `array.Contains(x.Id)` is now translated; the `((IEnumerable<string>)ids).Contains(…)` workaround
  keeps working and is no longer needed.

---

## Requirements

.NET 10 · `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`

## License

MIT
