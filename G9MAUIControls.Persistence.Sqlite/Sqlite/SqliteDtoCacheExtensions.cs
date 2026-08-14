using System.Diagnostics.CodeAnalysis;

namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Provides extension methods for retrieving cached data from a Sqlite repository.
/// </summary>
/// <remarks>
///     These methods allow for synchronous and asynchronous access to cached DTO data, ensuring that the
///     repository is not null before attempting to retrieve data.
/// </remarks>
public static class SqliteDtoCacheExtensions
{
    /// <summary>
    ///     Retrieves the cached data of type TDto from the specified repository.
    /// </summary>
    /// <remarks>
    ///     The repository must be properly initialized and its cache populated before calling this
    ///     method. Throws an ArgumentNullException if repository is null.
    /// </remarks>
    /// <typeparam name="TDto">
    ///     The type of data transfer object to retrieve. Must be a reference type with a parameterless
    ///     constructor.
    /// </typeparam>
    /// <param name="repository">The repository instance from which to retrieve cached data. Cannot be null.</param>
    /// <returns>A list of TDto objects containing the cached data. The list may be empty if no data is available.</returns>
    public static List<TDto> GetDtoCacheData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDto>(this SqliteRepository<TDto> repository)
        where TDto : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteRepository<TDto>.GetCacheData();
    }

    /// <summary>
    ///     Retrieves cached data for the specified Data Transfer Object (DTO) type from the SQLite repository
    ///     asynchronously.
    /// </summary>
    /// <remarks>
    ///     Ensure that the repository is properly initialized before calling this method. This method is
    ///     intended for use with repositories that implement caching for DTOs.
    /// </remarks>
    /// <typeparam name="TDto">The type of Data Transfer Object to retrieve. Must be a class with a parameterless constructor.</typeparam>
    /// <param name="repository">The SQLite repository instance from which to retrieve cached DTO data. Cannot be null.</param>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains a list of DTOs retrieved from the
    ///     cache.
    /// </returns>
    public static Task<List<TDto>> GetDtoCacheDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDto>(this SqliteRepository<TDto> repository)
        where TDto : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteRepository<TDto>.GetCacheDataAsync();
    }

    /// <summary>
    ///     Retrieves the cached data transfer objects (DTOs) for the specified entity type from the SQLite repository.
    /// </summary>
    /// <remarks>
    ///     Throws an ArgumentNullException if the repository parameter is null. Ensure that the
    ///     repository is properly initialized before calling this method.
    /// </remarks>
    /// <typeparam name="TEntity">
    ///     The type of the entity for which cached DTOs are retrieved. Must be a class with a
    ///     parameterless constructor.
    /// </typeparam>
    /// <typeparam name="TDto">
    ///     The type of the data transfer object associated with the entity. Must be a class with a parameterless
    ///     constructor.
    /// </typeparam>
    /// <param name="repository">The SQLite repository instance from which to retrieve cached DTO data. Cannot be null.</param>
    /// <returns>A list of cached DTOs of type TDto associated with the specified entity type TEntity.</returns>
    public static List<TDto> GetDtoCacheData<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDto>(this SqliteRepository<TEntity> repository)
        where TEntity : class, new()
        where TDto : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteDtoCache<TEntity, TDto>.GetCacheData();
    }

    /// <summary>
    ///     Retrieves cached data as a list of DTOs from the specified SQLite repository asynchronously.
    /// </summary>
    /// <remarks>
    ///     This method leverages caching to improve performance by avoiding repeated database access.
    ///     The repository must be properly initialized and not null.
    /// </remarks>
    /// <typeparam name="TEntity">
    ///     The type of the entity stored in the SQLite repository. Must be a reference type with a parameterless
    ///     constructor.
    /// </typeparam>
    /// <typeparam name="TDto">
    ///     The type of the Data Transfer Object (DTO) to return. Must be a reference type with a
    ///     parameterless constructor.
    /// </typeparam>
    /// <param name="repository">The SQLite repository instance from which to retrieve cached DTO data. Cannot be null.</param>
    /// <returns>
    ///     A task representing the asynchronous operation. The task result contains a list of DTOs retrieved from the
    ///     cache.
    /// </returns>
    public static Task<List<TDto>> GetDtoCacheDataAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEntity, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TDto>(this SqliteRepository<TEntity> repository)
        where TEntity : class, new()
        where TDto : class, new()
    {
        ArgumentNullException.ThrowIfNull(repository);
        return SqliteDtoCache<TEntity, TDto>.GetCacheDataAsync();
    }
}