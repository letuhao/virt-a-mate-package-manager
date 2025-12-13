using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for VAR package operations.
/// </summary>
public interface IVarPackageRepository : IRepository<VarPackage>
{
    /// <summary>
    /// Gets a VAR package by its unique VAR name.
    /// </summary>
    Task<VarPackage?> GetByVarNameAsync(string varName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a VAR package exists by VAR name.
    /// </summary>
    Task<bool> ExistsByVarNameAsync(string varName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all VAR packages for a specific repository.
    /// </summary>
    Task<IEnumerable<VarPackage>> GetByRepositoryIdAsync(int repositoryId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets VAR packages by creator name.
    /// </summary>
    Task<IEnumerable<VarPackage>> GetByCreatorAsync(string creatorName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets VAR packages by creator and package name.
    /// </summary>
    Task<IEnumerable<VarPackage>> GetByCreatorAndPackageAsync(string creatorName, string packageName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets VAR packages by file path.
    /// </summary>
    Task<VarPackage?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Searches VAR packages with criteria.
    /// </summary>
    Task<IEnumerable<VarPackage>> SearchAsync(
        string? creatorName = null,
        string? packageName = null,
        string? version = null,
        string? licenseType = null,
        int? repositoryId = null,
        bool? isInstalled = null,
        CancellationToken cancellationToken = default);
}

