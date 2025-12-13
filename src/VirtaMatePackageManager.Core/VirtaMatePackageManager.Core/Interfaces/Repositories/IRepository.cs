namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Generic repository interface for basic CRUD operations.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>
    /// Gets an entity by ID.
    /// </summary>
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all entities.
    /// </summary>
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a new entity.
    /// </summary>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an entity by ID.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes an entity.
    /// </summary>
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an entity exists by ID.
    /// </summary>
    Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);
}

