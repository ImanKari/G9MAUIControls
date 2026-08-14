using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

// Central registry to refresh entity caches and associated DTO caches by type.
public static class SqliteRepositoryCacheRegistry
{
    private static readonly Lock RegistryLock = new();
    private static readonly Dictionary<Type, Func<Task>> HardRefreshActions = new();
    private static readonly Dictionary<Type, Action> ResetActions = new();
    private static readonly Dictionary<Type, List<Func<Task>>> EntityDtoCacheActions = new();

    public static void Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType,
        Func<Task> hardRefreshAction,
        Action? resetAction = null)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(hardRefreshAction);

        lock (RegistryLock)
        {
            HardRefreshActions[entityType] = hardRefreshAction;
            if (resetAction is not null)
            {
                ResetActions[entityType] = resetAction;
            }
        }
    }

    // Registers a DTO cache refresh action associated with a source entity type.
    // The dtoCacheType is used as a unique key in the main refresh dictionary.
    public static void RegisterDtoCacheForEntity(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type dtoCacheType,
        Func<Task> refreshAction,
        Action? resetAction = null)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(dtoCacheType);
        ArgumentNullException.ThrowIfNull(refreshAction);

        lock (RegistryLock)
        {
            HardRefreshActions[dtoCacheType] = refreshAction;
            if (resetAction is not null)
            {
                ResetActions[dtoCacheType] = resetAction;
            }

            if (!EntityDtoCacheActions.TryGetValue(entityType, out var actions))
            {
                actions = [];
                EntityDtoCacheActions[entityType] = actions;
            }

            actions.Add(refreshAction);
        }
    }

    public static void ResetAllCachesForSession()
    {
        Action[] resetActions;
        lock (RegistryLock)
        {
            resetActions = ResetActions.Values.ToArray();
        }

        foreach (var resetAction in resetActions)
        {
            resetAction();
        }
    }

    public static async Task HardRefreshAllCacheAsync()
    {
        Func<Task>[] hardRefreshActions;
        lock (RegistryLock)
        {
            hardRefreshActions = HardRefreshActions.Values.ToArray();
        }

        foreach (var hardRefreshAction in hardRefreshActions)
        {
            await hardRefreshAction().ConfigureAwait(false);
        }
    }

    public static async Task<bool> RefreshCacheAsync(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        Func<Task>? refreshTask;
        lock (RegistryLock)
        {
            if (!HardRefreshActions.TryGetValue(entityType, out refreshTask))
            {
                return false;
            }
        }

        await refreshTask!().ConfigureAwait(false);
        return true;
    }

    // Refreshes the entity cache and all associated DTO caches for the given entity type.
    public static async Task RefreshEntityAndDtoCachesAsync(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
        Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);

        Func<Task>? entityRefresh;
        Func<Task>[]? dtoCacheActions;

        lock (RegistryLock)
        {
            HardRefreshActions.TryGetValue(entityType, out entityRefresh);

            dtoCacheActions = EntityDtoCacheActions.TryGetValue(entityType, out var actions)
                ? actions.ToArray()
                : null;
        }

        if (entityRefresh is not null)
        {
            await entityRefresh().ConfigureAwait(false);
        }

        if (dtoCacheActions is not null)
        {
            foreach (var action in dtoCacheActions)
            {
                await action().ConfigureAwait(false);
            }
        }
    }
}