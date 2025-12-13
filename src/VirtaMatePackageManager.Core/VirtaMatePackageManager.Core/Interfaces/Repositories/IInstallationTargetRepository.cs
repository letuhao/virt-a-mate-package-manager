using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for InstallationTarget entity operations.
/// </summary>
public interface IInstallationTargetRepository : IRepository<InstallationTarget>
{
    /// <summary>
    /// Gets all active installation targets.
    /// </summary>
    Task<IEnumerable<InstallationTarget>> GetActiveTargetsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets installation target by path.
    /// </summary>
    Task<InstallationTarget?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the default installation target.
    /// </summary>
    Task<InstallationTarget?> GetDefaultTargetAsync(CancellationToken cancellationToken = default);
}

