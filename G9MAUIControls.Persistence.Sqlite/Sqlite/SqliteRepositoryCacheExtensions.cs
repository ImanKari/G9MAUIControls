using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

// Instance wrappers over static cache APIs.
public static class SqliteRepositoryCacheExtensions
{
    public static List<T> GetCacheData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(this SqliteRepository<T> repository) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteRepository<T>.GetCacheData();
    }

    public static Task<List<T>> GetCacheDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(this SqliteRepository<T> repository) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteRepository<T>.GetCacheDataAsync();
    }
}
