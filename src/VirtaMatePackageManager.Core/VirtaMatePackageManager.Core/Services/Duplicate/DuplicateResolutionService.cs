using Entities = VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Core.Services.Duplicate;

/// <summary>
/// Options for resolving duplicate VAR files.
/// </summary>
public class DuplicateResolutionOptions
{
    /// <summary>
    /// Strategy for selecting primary file when multiple copies exist.
    /// </summary>
    public PrimaryFileSelectionStrategy SelectionStrategy { get; set; } = PrimaryFileSelectionStrategy.HighestPriorityRepository;
    
    /// <summary>
    /// Whether to fail if same VAR name has different content (different hash).
    /// </summary>
    public bool StrictMode { get; set; } = false;
}

/// <summary>
/// Service for resolving which VAR file to use when duplicates exist.
/// </summary>
public class DuplicateResolutionService
{
    private readonly IUnitOfWork _unitOfWork;

    public DuplicateResolutionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Resolves which VAR file to use when multiple copies exist.
    /// Since VAR name is unique in database (UNIQUE constraint), this queries by VAR name
    /// and returns the single record. VAR name is the unique identifier.
    /// </summary>
    /// <param name="varName">VAR name to resolve (e.g., "Creator.Package.1.var")</param>
    /// <param name="options">Resolution options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result with resolved VAR package</returns>
    public async Task<Result<Entities.VarPackage>> ResolveDuplicateVarFileAsync(
        string varName,
        DuplicateResolutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateResolutionOptions();
        
        // Query database - VAR name is unique key (UNIQUE constraint)
        // This will return at most 1 record
        var varPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, cancellationToken);
        
        if (varPackage == null)
        {
            return Result<Entities.VarPackage>.Failure(
                $"VAR package not found: {varName}",
                ErrorCode.VarPackageNotFound);
        }
        
        // VAR name is UNIQUE in database, so we only have one record
        // If multiple copies exist in file system, only the primary one (based on repository priority)
        // is indexed in the database during scanning
        
        return Result<Entities.VarPackage>.Success(varPackage);
    }
    
    /// <summary>
    /// Gets VAR packages by name (for cases where we want to check file system directly).
    /// Note: Database constraint ensures only one record per VAR name exists.
    /// This method is mainly for consistency with spec, but will return at most one record.
    /// </summary>
    private async Task<List<Entities.VarPackage>> GetVarPackagesByNameAsync(
        string varName,
        CancellationToken cancellationToken)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, cancellationToken);
        
        if (varPackage == null)
        {
            return new List<Entities.VarPackage>();
        }
        
        return new List<Entities.VarPackage> { varPackage };
    }
    
    /// <summary>
    /// Determines the primary VAR package from a list based on selection strategy.
    /// This would be used if we were checking file system for multiple copies.
    /// </summary>
    private async Task<Entities.VarPackage> DeterminePrimaryVarPackageAsync(
        List<Entities.VarPackage> varPackages,
        PrimaryFileSelectionStrategy strategy,
        CancellationToken cancellationToken)
    {
        // Get repository information for priority-based strategies
        var repositoryIds = varPackages.Select(v => v.RepositoryId).Distinct().ToList();
        var repositories = new Dictionary<int, Entities.Repository>();
        
        foreach (var repoId in repositoryIds)
        {
            var repo = await _unitOfWork.Repositories.GetByIdAsync(repoId, cancellationToken);
            if (repo != null)
            {
                repositories[repoId] = repo;
            }
        }
        
        return strategy switch
        {
            PrimaryFileSelectionStrategy.HighestPriorityRepository =>
                varPackages
                    .OrderByDescending(vp => repositories.TryGetValue(vp.RepositoryId, out var repo) ? repo.Priority : 0)
                    .ThenByDescending(vp => vp.FileModifiedAt ?? DateTime.MinValue)
                    .First(),
            
            PrimaryFileSelectionStrategy.NewestFile =>
                varPackages.OrderByDescending(vp => vp.FileModifiedAt ?? DateTime.MinValue).First(),
            
            PrimaryFileSelectionStrategy.OldestFile =>
                varPackages.OrderBy(vp => vp.FileModifiedAt ?? DateTime.MaxValue).First(),
            
            PrimaryFileSelectionStrategy.LargestFile =>
                varPackages.OrderByDescending(vp => vp.FileSize).First(),
            
            PrimaryFileSelectionStrategy.SmallestFile =>
                varPackages.OrderBy(vp => vp.FileSize).First(),
            
            PrimaryFileSelectionStrategy.FirstFound =>
                varPackages.First(),
            
            _ =>
                varPackages
                    .OrderByDescending(vp => repositories.TryGetValue(vp.RepositoryId, out var repo) ? repo.Priority : 0)
                    .ThenByDescending(vp => vp.FileModifiedAt ?? DateTime.MinValue)
                    .First()
        };
    }
}

