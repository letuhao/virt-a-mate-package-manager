namespace VirtaMatePackageManager.Core.Interfaces.Repositories;

/// <summary>
/// Unit of Work pattern interface for managing transactions and coordinating repositories.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// VAR package repository.
    /// </summary>
    IVarPackageRepository VarPackages { get; }
    
    /// <summary>
    /// Repository repository.
    /// </summary>
    IRepositoryRepository Repositories { get; }
    
    /// <summary>
    /// Installation target repository.
    /// </summary>
    IInstallationTargetRepository InstallationTargets { get; }
    
    /// <summary>
    /// Dependency repository.
    /// </summary>
    IDependencyRepository Dependencies { get; }
    
    /// <summary>
    /// Installation repository.
    /// </summary>
    IInstallationRepository Installations { get; }
    
    /// <summary>
    /// Saves all changes made in this unit of work.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}

