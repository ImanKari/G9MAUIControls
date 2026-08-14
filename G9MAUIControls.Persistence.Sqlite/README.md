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
(a geodatabase, an import) has its own. Every id column this package creates is `COLLATE NOCASE`, so
anything expressed as a **query** is already correct. But once rows are objects, `==`,
`FirstOrDefault(x => x.Id == id)`, `Dictionary<string, …>` and `HashSet<string>` are **ordinal**, and
silently return "no match".

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

var id       = SqliteEntityAuditDefaults.CreateNewId();   // canonical: upper-case GUID, no braces
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
- **Ordered migrations**, and built-in `SQLITE_BUSY` back-off retry — not something each caller reimplements.

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
          // Migrations and interceptors DO take a type argument (new() constraint), or an instance.
          .AddMigration<Migration_001>()
          .Entity<Sample>(e => e
              .HasGuidId(x => x.SamplingId)
              .SoftDelete(x => x.IsDeleted)
              .AlwaysFilter(x => !x.IsDeleted)
              .Index(x => x.SamplingId)
              .Cache(G9CachePolicy.Debounced(250)))
          .AddInterceptor<SyncMetadataInterceptor>();      // per-write hooks
});
```

Audit fields, soft delete, tenancy, sync metadata, conflict policy, value converters, indexes and
per-entity rules all arrive through the builder, the entity descriptors, and the interceptor pipeline. The
package knows nothing about your entities or your business rules — and deliberately knows nothing about
any particular sync framework.

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

A `TrimmerRootDescriptor` ships in the package to help.

---

## Requirements

.NET 10 · `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`

## License

MIT
