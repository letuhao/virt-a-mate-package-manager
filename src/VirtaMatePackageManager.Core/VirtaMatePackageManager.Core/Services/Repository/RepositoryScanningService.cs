using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.Services.Parsing;
using VirtaMatePackageManager.Core.Services.Validation;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Services.Repository;

/// <summary>
/// Result of scanning a repository.
/// </summary>
public class ScanResult
{
    public int RepositoryId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int FilesScanned { get; set; }
    public int FilesAdded { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesRemoved { get; set; }
    public int FilesSkipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
}

/// <summary>
/// Options for scanning a repository.
/// </summary>
public class ScanOptions
{
    /// <summary>
    /// Whether to compute file hashes during scan (slower but more accurate).
    /// </summary>
    public bool ComputeHashes { get; set; } = true;

    /// <summary>
    /// Whether to scan recursively into subdirectories.
    /// </summary>
    public bool Recursive { get; set; } = true;

    /// <summary>
    /// Whether to update existing VAR packages if files have changed.
    /// </summary>
    public bool UpdateExisting { get; set; } = true;

    /// <summary>
    /// Whether to mark VAR packages as removed if files no longer exist.
    /// </summary>
    public bool MarkRemoved { get; set; } = true;

    /// <summary>
    /// Directories to exclude from scanning (e.g., "redundant", "invalid").
    /// </summary>
    public HashSet<string> ExcludeDirectories { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "redundant",
        "invalid",
        "tidied",
        "stale",
        "old_version",
        "deleted"
    };
}

/// <summary>
/// Information about a VAR file found during file system scan.
/// </summary>
public class ScannedVarFile
{
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public string RelativePath { get; set; } = string.Empty;
}

/// <summary>
/// Progress callback for repository scanning.
/// </summary>
public delegate void ScanProgressCallback(int processed, int total, string currentFile);

/// <summary>
/// Service for scanning repositories to discover and index VAR files.
/// </summary>
public class RepositoryScanningService
{
    private readonly FileHashService _fileHashService;
    private readonly SymbolicLinkService _symbolicLinkService;
    private readonly VarFileValidationService _validationService;
    private readonly VarFileParsingService _parsingService;

    public RepositoryScanningService(
        FileHashService? fileHashService = null,
        SymbolicLinkService? symbolicLinkService = null,
        VarFileValidationService? validationService = null,
        VarFileParsingService? parsingService = null)
    {
        _fileHashService = fileHashService ?? new FileHashService();
        _symbolicLinkService = symbolicLinkService ?? new SymbolicLinkService();
        _validationService = validationService ?? new VarFileValidationService();
        _parsingService = parsingService ?? new VarFileParsingService();
    }

    /// <summary>
    /// Scans the file system for VAR files in the specified directory.
    /// </summary>
    /// <param name="repositoryPath">Root path of the repository</param>
    /// <param name="options">Scan options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of scanned VAR files</returns>
    public List<ScannedVarFile> ScanFileSystemForVarFiles(
        string repositoryPath,
        ScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path cannot be empty", nameof(repositoryPath));
        }

        if (!Directory.Exists(repositoryPath))
        {
            return new List<ScannedVarFile>();
        }

        options ??= new ScanOptions();
        var varFiles = new List<ScannedVarFile>();
        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            var allFiles = Directory.GetFiles(repositoryPath, "*.var", searchOption);

            foreach (var filePath in allFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Skip excluded directories
                var relativePath = Path.GetRelativePath(repositoryPath, filePath);
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (pathParts.Any(part => options.ExcludeDirectories.Contains(part)))
                {
                    continue;
                }

                // Skip symlinks - use FileInfo to check
                try
                {
                    if (_symbolicLinkService.IsSymbolicLink(filePath))
                    {
                        continue;
                    }
                }
                catch
                {
                    // If we can't check, continue anyway
                }

                // Get file info
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    
                    if (!fileInfo.Exists)
                    {
                        continue;
                    }

                    varFiles.Add(new ScannedVarFile
                    {
                        Path = filePath,
                        Size = fileInfo.Length,
                        LastWriteTime = fileInfo.LastWriteTimeUtc,
                        CreatedTime = fileInfo.CreationTimeUtc,
                        RelativePath = relativePath
                    });
                }
                catch (Exception ex)
                {
                    // Skip files that can't be accessed
                    System.Diagnostics.Debug.WriteLine($"Failed to get file info for {filePath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to scan directory {repositoryPath}: {ex.Message}");
        }

        return varFiles;
    }
}

