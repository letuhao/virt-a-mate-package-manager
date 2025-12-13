using Entities = VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Core.Services.Duplicate;

/// <summary>
/// Information about a VAR file for duplicate detection.
/// </summary>
public class VarFileInfo
{
    public int VarPackageId { get; set; }
    public string Path { get; set; } = string.Empty;
    public int RepositoryId { get; set; }
    public string RepositoryName { get; set; } = string.Empty;
    public int RepositoryPriority { get; set; }
    public string VarName { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public long Size { get; set; }
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Type of duplicate.
/// </summary>
public enum DuplicateType
{
    /// <summary>
    /// Multiple files with same VAR name (filename duplicate).
    /// </summary>
    FilenameDuplicate = 1,
    
    /// <summary>
    /// Files with different names but same content (hash duplicate).
    /// </summary>
    ContentDuplicate = 2
}

/// <summary>
/// Strategy for selecting the primary file from duplicates.
/// </summary>
public enum PrimaryFileSelectionStrategy
{
    /// <summary>
    /// File from repository with highest priority.
    /// </summary>
    HighestPriorityRepository = 1,
    
    /// <summary>
    /// Most recently modified file.
    /// </summary>
    NewestFile = 2,
    
    /// <summary>
    /// Oldest file (might be original).
    /// </summary>
    OldestFile = 3,
    
    /// <summary>
    /// Largest file size.
    /// </summary>
    LargestFile = 4,
    
    /// <summary>
    /// Smallest file size.
    /// </summary>
    SmallestFile = 5,
    
    /// <summary>
    /// First file encountered during scan.
    /// </summary>
    FirstFound = 6
}

/// <summary>
/// Options for duplicate detection.
/// </summary>
public class DuplicateDetectionOptions
{
    /// <summary>
    /// Strategy for selecting primary file.
    /// </summary>
    public PrimaryFileSelectionStrategy SelectionStrategy { get; set; } = PrimaryFileSelectionStrategy.HighestPriorityRepository;
    
    /// <summary>
    /// Whether to detect content duplicates (same hash, different names).
    /// </summary>
    public bool DetectContentDuplicates { get; set; } = false;
    
    /// <summary>
    /// Minimum wasted space to report (in bytes).
    /// </summary>
    public long MinWasteSizeToReport { get; set; } = 0;
}

/// <summary>
/// A group of duplicate files.
/// </summary>
public class DuplicateGroup
{
    /// <summary>
    /// Type of duplicate.
    /// </summary>
    public DuplicateType Type { get; set; }
    
    /// <summary>
    /// VAR name (null for content duplicates with multiple names).
    /// </summary>
    public string? VarName { get; set; }
    
    /// <summary>
    /// Primary file (selected as the one to keep).
    /// </summary>
    public VarFileInfo PrimaryFile { get; set; } = null!;
    
    /// <summary>
    /// List of duplicate files.
    /// </summary>
    public List<VarFileInfo> DuplicateFiles { get; set; } = new();
    
    /// <summary>
    /// Total wasted space (in bytes).
    /// </summary>
    public long TotalWastedSpace { get; set; }
    
    /// <summary>
    /// Alternative VAR names (for content duplicates).
    /// </summary>
    public List<string>? AlternativeNames { get; set; }
}

/// <summary>
/// Result of duplicate detection.
/// </summary>
public class DuplicateDetectionResult
{
    /// <summary>
    /// Filename duplicates (same VAR name in multiple repositories).
    /// </summary>
    public List<DuplicateGroup> Duplicates { get; set; } = new();
    
    /// <summary>
    /// Content duplicates (same hash, different names).
    /// </summary>
    public List<DuplicateGroup> ContentDuplicates { get; set; } = new();
    
    /// <summary>
    /// Total number of duplicate groups.
    /// </summary>
    public int TotalDuplicates => Duplicates.Count + ContentDuplicates.Count;
    
    /// <summary>
    /// Total wasted space across all duplicates (in bytes).
    /// </summary>
    public long TotalWastedSpace { get; set; }
}

/// <summary>
/// Service for detecting duplicate VAR files across repositories.
/// </summary>
public class DuplicateDetectionService
{
    private readonly IUnitOfWork _unitOfWork;

    public DuplicateDetectionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Detects duplicate VAR files across multiple repositories.
    /// Since VAR name is globally unique, we detect files with same VAR name in different repositories.
    /// </summary>
    /// <param name="repositories">List of repositories to check</param>
    /// <param name="options">Detection options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Duplicate detection result</returns>
    public async Task<DuplicateDetectionResult> DetectDuplicateVarFilesAsync(
        IEnumerable<Entities.Repository> repositories,
        DuplicateDetectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DuplicateDetectionOptions();
        var result = new DuplicateDetectionResult();
        
        // Build map by VAR name (which is unique identifier)
        var varNameMap = new Dictionary<string, List<VarFileInfo>>();
        
        // Scan all repositories and group by VAR name
        foreach (var repository in repositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!repository.Enabled)
            {
                continue;
            }
            
            // Get all VAR packages from database for this repository
            var varPackages = await _unitOfWork.VarPackages.GetByRepositoryIdAsync(
                repository.Id,
                cancellationToken);
            
            foreach (var varPackage in varPackages)
            {
                var varName = varPackage.VarName; // e.g., "Creator.Package.1.var"
                
                if (!varNameMap.ContainsKey(varName))
                {
                    varNameMap[varName] = new List<VarFileInfo>();
                }
                
                varNameMap[varName].Add(new VarFileInfo
                {
                    VarPackageId = varPackage.Id,
                    Path = varPackage.FilePath,
                    RepositoryId = repository.Id,
                    RepositoryName = repository.Name,
                    RepositoryPriority = repository.Priority,
                    VarName = varName,
                    FileHash = varPackage.FileHash,
                    Size = varPackage.FileSize,
                    LastModified = varPackage.FileModifiedAt
                });
            }
        }
        
        // Find VAR names with multiple copies
        foreach (var kvp in varNameMap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var varName = kvp.Key;
            var files = kvp.Value;
            
            if (files.Count > 1)
            {
                // Same VAR name exists in multiple repositories
                // Note: This should not happen in database due to UNIQUE constraint,
                // but can happen if scanning file system directly or before constraint is enforced
                var primaryFile = DeterminePrimaryFile(files, options.SelectionStrategy);
                
                var wastedSpace = (files.Count - 1) * primaryFile.Size;
                
                // Filter by minimum waste size
                if (wastedSpace >= options.MinWasteSizeToReport)
                {
                    var duplicateGroup = new DuplicateGroup
                    {
                        Type = DuplicateType.FilenameDuplicate,
                        VarName = varName,
                        PrimaryFile = primaryFile,
                        DuplicateFiles = files.Where(f => f.Path != primaryFile.Path).ToList(),
                        TotalWastedSpace = wastedSpace
                    };
                    
                    result.Duplicates.Add(duplicateGroup);
                }
            }
        }
        
        // Optional: Detect content duplicates (same hash, different names)
        if (options.DetectContentDuplicates)
        {
            var hashMap = new Dictionary<string, List<VarFileInfo>>();
            
            // Build hash map
            foreach (var files in varNameMap.Values)
            {
                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(file.FileHash))
                    {
                        if (!hashMap.ContainsKey(file.FileHash))
                        {
                            hashMap[file.FileHash] = new List<VarFileInfo>();
                        }
                        
                        hashMap[file.FileHash].Add(file);
                    }
                }
            }
            
            // Find content duplicates (same hash, different names)
            foreach (var kvp in hashMap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var hash = kvp.Key;
                var files = kvp.Value;
                
                if (files.Count > 1)
                {
                    var varNames = files.Select(f => f.VarName).Distinct().ToList();
                    
                    if (varNames.Count > 1)
                    {
                        // Same content, different VAR names (rare but possible)
                        var wastedSpace = (files.Count - 1) * files[0].Size;
                        
                        if (wastedSpace >= options.MinWasteSizeToReport)
                        {
                            var duplicateGroup = new DuplicateGroup
                            {
                                Type = DuplicateType.ContentDuplicate,
                                VarName = null, // Multiple names
                                PrimaryFile = files[0], // First one
                                DuplicateFiles = files.Skip(1).ToList(),
                                TotalWastedSpace = wastedSpace,
                                AlternativeNames = varNames
                            };
                            
                            result.ContentDuplicates.Add(duplicateGroup);
                        }
                    }
                }
            }
        }
        
        // Calculate statistics
        result.TotalWastedSpace = result.Duplicates.Sum(d => d.TotalWastedSpace) +
                                  result.ContentDuplicates.Sum(d => d.TotalWastedSpace);
        
        return result;
    }

    /// <summary>
    /// Determines the primary file from a list of duplicates based on selection strategy.
    /// </summary>
    /// <param name="files">List of duplicate files</param>
    /// <param name="strategy">Selection strategy</param>
    /// <returns>Primary file</returns>
    public static VarFileInfo DeterminePrimaryFile(
        List<VarFileInfo> files,
        PrimaryFileSelectionStrategy strategy)
    {
        return strategy switch
        {
            PrimaryFileSelectionStrategy.HighestPriorityRepository =>
                files.OrderByDescending(f => f.RepositoryPriority)
                     .ThenByDescending(f => f.LastModified ?? DateTime.MinValue)
                     .First(),
            
            PrimaryFileSelectionStrategy.NewestFile =>
                files.OrderByDescending(f => f.LastModified ?? DateTime.MinValue).First(),
            
            PrimaryFileSelectionStrategy.OldestFile =>
                files.OrderBy(f => f.LastModified ?? DateTime.MaxValue).First(),
            
            PrimaryFileSelectionStrategy.LargestFile =>
                files.OrderByDescending(f => f.Size).First(),
            
            PrimaryFileSelectionStrategy.SmallestFile =>
                files.OrderBy(f => f.Size).First(),
            
            PrimaryFileSelectionStrategy.FirstFound =>
                files.First(),
            
            _ =>
                files.OrderByDescending(f => f.RepositoryPriority)
                     .ThenByDescending(f => f.LastModified ?? DateTime.MinValue)
                     .First()
        };
    }
}

