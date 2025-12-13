using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Repository interface for Dependency entity operations.
/// </summary>
public interface IDependencyRepository : IRepository<Dependency>
{
    /// <summary>
    /// Gets all dependencies for a VAR package.
    /// </summary>
    Task<IEnumerable<Dependency>> GetByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all unresolved dependencies.
    /// </summary>
    Task<IEnumerable<Dependency>> GetUnresolvedDependenciesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets dependencies by dependency name.
    /// </summary>
    Task<IEnumerable<Dependency>> GetByDependencyNameAsync(string dependencyName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all dependencies for a specific VAR package that are unresolved.
    /// </summary>
    Task<IEnumerable<Dependency>> GetUnresolvedByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default);
}

