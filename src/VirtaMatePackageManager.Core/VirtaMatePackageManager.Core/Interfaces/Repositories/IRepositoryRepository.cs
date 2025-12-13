using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Repository entity operations.
/// </summary>
public interface IRepositoryRepository : IRepository<Repository>
{
    /// <summary>
    /// Gets all enabled repositories.
    /// </summary>
    Task<IEnumerable<Repository>> GetEnabledRepositoriesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets repositories ordered by priority (ascending).
    /// </summary>
    Task<IEnumerable<Repository>> GetByPriorityAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets repository by path.
    /// </summary>
    Task<Repository?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
}

