using G9MAUIControls.Persistence.Sqlite;

namespace G9MAUIControls.Persistence.Sqlite;

public static class SqliteEntityAuditDefaults
{
    private static string? _cachedCurrentUserId;

    public static void ApplyInsertDefaults(
        IG9AuditedEntity entity,
        DateTime now,
        string? currentUserId)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var id = entity.Id.NormalizeSqliteGuidId();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = CreateNewId();
        }

        entity.Id = id;
        entity.CreatedTime = now;
        entity.UpdatedTime = now;

        if (!string.IsNullOrWhiteSpace(currentUserId))
        {
            entity.CreatedByUserId = currentUserId;
            entity.UpdatedByUserId = currentUserId;
            return;
        }

        var fallbackUserId = entity.CreatedByUserId ?? entity.UpdatedByUserId;
        entity.CreatedByUserId = fallbackUserId;
        entity.UpdatedByUserId = fallbackUserId;
    }

    public static void ApplyUpdateDefaults(
        IG9AuditedEntity entity,
        DateTime now,
        string? currentUserId)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var id = entity.Id.NormalizeSqliteGuidId();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = CreateNewId();
        }

        entity.Id = id;
        entity.UpdatedTime = now;

        if (!string.IsNullOrWhiteSpace(currentUserId))
        {
            entity.UpdatedByUserId = currentUserId;
        }
    }

    /// <summary>
    ///     The user id to stamp on audit columns, from the configured provider.
    /// </summary>
    /// <remarks>
    ///     The source app cached this in a static field and reached into its auth service on a miss. Both
    ///     halves were wrong for a library: the cache meant a second user on a shared device inherited the
    ///     first user's id, and the reach-in hard-wired one app's auth. The provider is asked on every write
    ///     precisely so a sign-in change is picked up immediately.
    /// </remarks>
    public static string? ResolveCurrentUserId(IG9CurrentUserProvider provider)
    {
        try
        {
            return provider.GetCurrentUserId().NormalizeSqliteGuidId();
        }
        catch (Exception)
        {
            // A consumer's provider must not be able to fail a write. An unattributed row is recoverable;
            // a lost row is not.
            return null;
        }
    }

    public static string CreateNewId()
    {
        return SqliteGuidStringNormalizer.CreateNewId();
    }
}
