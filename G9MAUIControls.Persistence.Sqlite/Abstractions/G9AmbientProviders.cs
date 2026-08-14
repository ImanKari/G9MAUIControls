using System.Diagnostics.CodeAnalysis;
using Microsoft.Maui.Storage;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Supplies "now" for audit columns.
///     <para>
///         <b>An interface read at write time, not a value captured at registration.</b> That distinction
///         is the whole point: a captured <see cref="DateTime" /> would freeze at DI-graph construction,
///         and the failure surfaces weeks later in timestamps nobody re-reads.
///     </para>
///     <para>
///         The library never assumes a timezone. Return UTC, device local, or a fixed business timezone —
///         whatever the app considers authoritative. Pick once and do not change it on a shipped app: rows
///         already written carry the old interpretation and nothing will migrate them.
///     </para>
/// </summary>
public interface IG9Clock
{
    /// <summary>The value written to created/updated audit columns.</summary>
    DateTime Now();
}

/// <summary>UTC. The default when nothing is configured, and the right answer for most apps.</summary>
public sealed class G9SystemClock : IG9Clock
{
    /// <inheritdoc />
    public DateTime Now() => DateTime.UtcNow;
}

/// <summary>
///     Supplies the current user id for audit columns.
///     <para>
///         Also an interface read at write time, and for a sharper reason than the clock: a shared device
///         changes user while the same repository instances live on. A captured id would stamp every later
///         row with whoever happened to be signed in when the graph was built.
///     </para>
/// </summary>
public interface IG9CurrentUserProvider
{
    /// <summary>
    ///     The signed-in user's id, or <c>null</c>.
    ///     <para>
    ///         <b><c>null</c> is a legitimate answer and is handled.</b> On insert the entity keeps whatever
    ///         it already carried rather than being overwritten with nothing — so a row written by a
    ///         background job before sign-in is not retroactively attributed to nobody.
    ///     </para>
    /// </summary>
    string? GetCurrentUserId();
}

/// <summary>No user. The default; audit user columns are left as the entity supplied them.</summary>
public sealed class G9NoCurrentUser : IG9CurrentUserProvider
{
    /// <inheritdoc />
    public string? GetCurrentUserId() => null;
}

/// <summary>
///     Decides which database file is open, and says when the answer changes.
///     <para>
///         A <b>strategy</b>, not a value, because the implementations share no code — only a question.
///         Single-file, per-user, per-tenant and in-memory are all valid and have nothing in common.
///     </para>
/// </summary>
public interface IG9SqliteDatabaseLocator
{
    /// <summary>
    ///     The full path to the database file. Called on every connection acquisition, so it must be cheap;
    ///     returning a <b>different</b> path closes the old connection and opens the new one, which is what
    ///     makes user switching work without anything caching a stale connection.
    /// </summary>
    string GetDatabasePath();

    /// <summary>
    ///     Raise this when <see cref="GetDatabasePath" /> would now return something else. The provider
    ///     closes the current connection and resets every cache, so no row from the previous database can
    ///     survive in memory into the next one.
    /// </summary>
    event EventHandler? DatabasePathChanged;
}

/// <summary>
///     One database file at a fixed path under the app's data directory. The right default for a
///     single-user app.
/// </summary>
/// <param name="fileName">File name, created under <see cref="FileSystem.AppDataDirectory" />.</param>
public sealed class G9SingleFileDatabaseLocator(string fileName = "g9.db") : IG9SqliteDatabaseLocator
{
    /// <inheritdoc />
    public string GetDatabasePath() => Path.Combine(FileSystem.AppDataDirectory, fileName);

    /// <inheritdoc />
    /// <remarks>Never raised: the path is fixed.</remarks>
    public event EventHandler? DatabasePathChanged
    {
        add { /* fixed path — nothing to notify */ }
        remove { }
    }
}

/// <summary>
///     A separate database per user, under <c>&lt;AppData&gt;/users/&lt;id&gt;/</c>. A working reference
///     implementation for multi-user devices — and a cautionary one.
///     <para>
///         ⚠ <b>The directory name is derived from the user id, so its exact form is load-bearing forever.</b>
///         Android filesystems are case-sensitive. If the id casing this receives ever changes — a
///         normalisation tweak, a different source for the id — the app looks for a directory that does not
///         exist, creates a fresh empty one, and the user appears to have lost all their local data.
///     </para>
///     <para>
///         Pass ids through <see cref="G9SqliteOptions.CanonicalIdCase" />-consistent normalisation, decide
///         the casing before first release, and never change it afterwards.
///     </para>
/// </summary>
public sealed class G9PerUserDatabaseLocator : IG9SqliteDatabaseLocator
{
    private readonly Func<string?> _userIdAccessor;
    private readonly string _fileName;

    /// <summary>Creates the locator.</summary>
    /// <param name="userIdAccessor">
    ///     Returns the active user's id, or <c>null</c> when nobody is signed in — in which case
    ///     <see cref="GetDatabasePath" /> throws rather than quietly opening a shared database.
    /// </param>
    /// <param name="fileName">Database file name inside each user's directory.</param>
    public G9PerUserDatabaseLocator(Func<string?> userIdAccessor, string fileName = "g9.db")
    {
        ArgumentNullException.ThrowIfNull(userIdAccessor);
        _userIdAccessor = userIdAccessor;
        _fileName = fileName;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    ///     No active user. Thrown rather than falling back to a shared file: silently writing one user's
    ///     rows into another's database is far worse than a loud failure at startup.
    /// </exception>
    public string GetDatabasePath()
    {
        var userId = _userIdAccessor();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException(
                "G9PerUserDatabaseLocator has no active user. Activate the user's partition before any " +
                "repository opens a connection — see 04-SqlitePersistence.md.");
        }

        var safe = string.Concat(userId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        var dir = Path.Combine(FileSystem.AppDataDirectory, "users", safe);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, _fileName);
    }

    /// <inheritdoc />
    public event EventHandler? DatabasePathChanged;

    /// <summary>
    ///     Call after the active user changes, so the provider swaps the connection and drops every cache.
    /// </summary>
    public void NotifyUserChanged() => DatabasePathChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
///     Marks an entity whose audit columns the library maintains.
///     <para>
///         Implement it and the library sets <see cref="CreatedTime" /> / <see cref="UpdatedTime" /> and the
///         two user columns on insert, refreshes the update pair on update, and mints an
///         <see cref="Id" /> when one is missing. <b>Do not set any of them by hand</b> — a manual value is
///         overwritten, so code that sets them reads as working while doing nothing.
///     </para>
/// </summary>
public interface IG9AuditedEntity
{
    /// <summary>Primary key. A canonical GUID string, minted on insert when empty.</summary>
    string Id { get; set; }

    /// <summary>Set once, on insert.</summary>
    DateTime CreatedTime { get; set; }

    /// <summary>Refreshed on every write.</summary>
    DateTime UpdatedTime { get; set; }

    /// <summary>Set on insert from <see cref="IG9CurrentUserProvider" />; left alone when that returns null.</summary>
    string? CreatedByUserId { get; set; }

    /// <summary>Refreshed on every write from <see cref="IG9CurrentUserProvider" />.</summary>
    string? UpdatedByUserId { get; set; }
}

/// <summary>
///     Converts a property between the shape your model wants and the shape SQLite can store.
///     <para>
///         For anything sqlite-net cannot map natively — an enum you want stored as text, a JSON blob, a
///         value object, a <see cref="TimeSpan" /> as ticks.
///     </para>
/// </summary>
/// <typeparam name="TModel">The property type on the entity.</typeparam>
/// <typeparam name="TStore">The stored type. Must be one sqlite-net maps natively.</typeparam>
public interface IG9ValueConverter<TModel, TStore>
{
    /// <summary>Model → stored.</summary>
    TStore ToStore(TModel value);

    /// <summary>Stored → model.</summary>
    TModel FromStore(TStore value);
}

/// <summary>
///     One ordered schema change.
///     <para>
///         <b><see cref="Version" /> is explicit rather than parsed from the type name.</b> Deriving it from
///         the name makes a rename a silent data-corruption bug, and makes gaps in the sequence (which are
///         normal — a migration gets abandoned) look like errors.
///     </para>
/// </summary>
public interface IG9SqliteMigration
{
    /// <summary>
    ///     Monotonic version. Applied in ascending order; anything at or below the database's recorded
    ///     version is skipped. Gaps are fine. Two migrations sharing a version is a configuration error and
    ///     is rejected at startup rather than resolved arbitrarily.
    /// </summary>
    long Version { get; }

    /// <summary>
    ///     Applies the change.
    ///     <para>
    ///         Runs inside a transaction, so a throw rolls the whole migration back and the recorded version
    ///         does not advance. Must be safe to re-run after such a failure.
    ///     </para>
    /// </summary>
    Task ApplyAsync(IG9SqliteMigrationContext context, CancellationToken cancellationToken);
}

/// <summary>What a migration is given to work with.</summary>
public interface IG9SqliteMigrationContext
{
    /// <summary>Executes DDL or DML. Parameters are passed positionally, as sqlite-net expects.</summary>
    Task<int> ExecuteAsync(string sql, params object[] parameters);

    /// <summary>Reads rows, for a migration that must inspect data before changing it.</summary>
    Task<List<T>> QueryAsync<T>(string sql, params object[] parameters) where T : new();

    /// <summary>
    ///     True when the column exists. Cheaper and clearer than catching the error from a duplicate
    ///     <c>ALTER TABLE ADD COLUMN</c>, which SQLite reports as a generic failure.
    /// </summary>
    Task<bool> ColumnExistsAsync(string table, string column);

    /// <summary>True when the table exists.</summary>
    Task<bool> TableExistsAsync(string table);
}

/// <summary>
///     Runs once per resolved database path, after migrations, before the first repository read.
///     <para>
///         For seeding reference data or enabling pragmas. Must be idempotent: it runs again for every new
///         database path, which on a per-user locator means once per user.
///     </para>
/// </summary>
public interface IG9SqliteInitializer
{
    /// <summary>Initialises the database at the current path.</summary>
    Task InitializeAsync(IG9SqliteMigrationContext context, CancellationToken cancellationToken);
}

/// <summary>
///     A stable <see cref="StringComparer" /> for GUID-id keys, and the shortest correct thing to type.
///     <para>
///         <b>Use it for every id-keyed <see cref="Dictionary{TKey,TValue}" />, <see cref="HashSet{T}" />,
///         <c>GroupBy</c>, <c>Distinct</c> and <c>Contains</c>.</b> Rows can arrive in any casing — a sync
///         engine writes server casing verbatim, a second store has its own — and while every id column the
///         library creates is <c>COLLATE NOCASE</c> (so SQL is safe), a plain C# comparison on materialised
///         rows is ordinal and silently returns "no match".
///     </para>
///     <para>
///         This exists so the correct option is also the convenient one. The library cannot enforce it:
///         once rows are objects, the comparison is consumer code.
///     </para>
/// </summary>
public static class G9IdComparer
{
    /// <summary>Case-insensitive ordinal. The only correct comparer for a GUID-id string.</summary>
    public static StringComparer Ordinal => StringComparer.OrdinalIgnoreCase;

    /// <summary>Compares two ids. ~22 ns, zero allocation — faster than normalising both operands.</summary>
    public static bool Equals(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Canonical casing for GUID strings the library writes.</summary>
public enum G9IdCase
{
    /// <summary>
    ///     Lowercase. The default, and matches <see cref="Guid.ToString()" />.
    /// </summary>
    Lower,

    /// <summary>
    ///     Uppercase. Only for compatibility with an existing store that already uses it — and note that if
    ///     anything derives a <b>file path</b> from a normalised id, changing this on a shipped app orphans
    ///     the old directory on case-sensitive filesystems.
    /// </summary>
    Upper
}

/// <summary>
///     Marks a <see cref="string" /> property that holds a GUID, so the library normalises it on write and
///     in query predicates and declares the column <c>COLLATE NOCASE</c>.
///     <para>
///         <b>Required — not inferred from the name.</b> Property names ending in "Id" are unreliable:
///         <c>NationalId</c>, <c>EconomicalId</c> and <c>ExternalId</c> are business text, and normalising
///         them would corrupt them. Only a property literally named <c>Id</c> is recognised automatically.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class G9GuidIdAttribute : Attribute;

/// <summary>
///     Reflection requirements for an entity type, in one place so the annotation is stated once.
/// </summary>
/// <remarks>
///     This package is NOT AOT-compatible and does not claim to be (ADR-0014): sqlite-net maps by
///     reflection. The annotation still earns its keep — it tells the trimmer to preserve the members
///     mapping needs, so a trimmed (non-AOT) app works.
/// </remarks>
public static class G9EntityReflection
{
    /// <summary>What the mapper touches on an entity type.</summary>
    public const DynamicallyAccessedMemberTypes Required =
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;
}
