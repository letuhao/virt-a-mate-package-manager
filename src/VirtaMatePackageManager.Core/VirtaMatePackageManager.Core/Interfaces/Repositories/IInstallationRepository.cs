using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Installation entity operations.
/// </summary>
public interface IInstallationRepository : IRepository<Installation>
{
    /// <summary>
    /// Gets all installations for a VAR package.
    /// </summary>
    Task<IEnumerable<Installation>> GetByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all installations for an installation target.
    /// </summary>
    Task<IEnumerable<Installation>> GetByTargetIdAsync(int installationTargetId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a specific installation by VAR package ID and installation target ID.
    /// </summary>
    Task<Installation?> GetInstallationAsync(int varPackageId, int installationTargetId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all enabled installations.
    /// </summary>
    Task<IEnumerable<Installation>> GetEnabledInstallationsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all enabled installations for a specific installation target.
    /// </summary>
    Task<IEnumerable<Installation>> GetEnabledByTargetIdAsync(int installationTargetId, CancellationToken cancellationToken = default);
}

