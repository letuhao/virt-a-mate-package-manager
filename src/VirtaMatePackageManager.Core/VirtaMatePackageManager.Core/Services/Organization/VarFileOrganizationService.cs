using Entities = VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.Services.Validation;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Services.Organization;

/// <summary>
/// Progress callback for organization operations.
/// </summary>
public delegate void OrganizationProgressCallback(int processed, int total, string currentFile);

/// <summary>
/// Information about a cross-repository conflict.
/// </summary>
public class CrossRepositoryConflictInfo
{
    public string VarName { get; set; } = string.Empty;
    public int ExistingRepositoryId { get; set; }
    public string ExistingRepositoryName { get; set; } = string.Empty;
    public string ExistingFilePath { get; set; } = string.Empty;
    public int NewRepositoryId { get; set; }
    public string NewFilePath { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}

/// <summary>
/// Result of organizing VAR files in a repository.
/// </summary>
public class OrganizationResult
{
    public List<string> MovedFiles { get; set; } = new();
    public List<string> InvalidFiles { get; set; } = new();
    public List<string> RedundantFiles { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
    public List<CrossRepositoryConflictInfo> CrossRepositoryConflicts { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Logs { get; set; } = new();
    
    public int TotalProcessed => MovedFiles.Count + InvalidFiles.Count + RedundantFiles.Count + SkippedFiles.Count;
    
    public void Log(string message)
    {
        Logs.Add($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {message}");
    }
}

/// <summary>
/// Service for organizing VAR files within repositories.
/// </summary>
public class VarFileOrganizationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly VarFileValidationService _validationService;
    private readonly FileHashService _fileHashService;
    private readonly SymbolicLinkService _symbolicLinkService;

    public VarFileOrganizationService(
        IUnitOfWork unitOfWork,
        VarFileValidationService? validationService = null,
        FileHashService? fileHashService = null,
        SymbolicLinkService? symbolicLinkService = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _validationService = validationService ?? new VarFileValidationService();
        _fileHashService = fileHashService ?? new FileHashService();
        _symbolicLinkService = symbolicLinkService ?? new SymbolicLinkService();
    }

    /// <summary>
    /// Organizes VAR files within a single repository.
    /// Handles local duplicates within the repository, but cross-repository duplicates
    /// are handled separately by DetectDuplicateVarFiles.
    /// </summary>
    /// <param name="repositoryId">Repository ID</param>
    /// <param name="varFiles">List of VAR file paths to organize</param>
    /// <param name="progressCallback">Progress callback (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Organization result</returns>
    public async Task<OrganizationResult> OrganizeVarFilesAsync(
        int repositoryId,
        IEnumerable<string> varFiles,
        OrganizationProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OrganizationResult();
        
        // Get repository
        var repository = await _unitOfWork.Repositories.GetByIdAsync(repositoryId, cancellationToken);
        if (repository == null)
        {
            result.Errors.Add($"Repository {repositoryId} not found");
            return result;
        }
        
        var varFilesList = varFiles.ToList();
        var total = varFilesList.Count;
        var processed = 0;
        
        // Create organization directories
        var tidyDir = Path.Combine(repository.Path, "tidied");
        var redundantDir = Path.Combine(repository.Path, "redundant");
        var invalidDir = Path.Combine(repository.Path, "invalid");
        
        // Ensure directories exist
        Directory.CreateDirectory(tidyDir);
        Directory.CreateDirectory(redundantDir);
        Directory.CreateDirectory(invalidDir);
        
        foreach (var varFile in varFilesList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            processed++;
            progressCallback?.Invoke(processed, total, varFile);
            
            // Skip symlinks
            try
            {
                if (_symbolicLinkService.IsSymbolicLink(varFile))
                {
                    result.SkippedFiles.Add(varFile);
                    result.Log($"Skipped symlink: {Path.GetFileName(varFile)}");
                    continue;
                }
            }
            catch
            {
                // If we can't check, continue anyway
            }
            
            // Validate filename
            var validation = _validationService.ValidateVarFileName(varFile);
            
            if (!validation.IsValid)
            {
                await MoveToInvalidAsync(varFile, invalidDir, result, cancellationToken);
                continue;
            }
            
            var parsedName = validation.ParsedName!;
            var varName = parsedName.Creator + "." + parsedName.Package + "." + parsedName.Version;
            
            // Check if VAR name already exists in database (UNIQUE constraint)
            var existingVarPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName + ".var", cancellationToken);
            
            if (existingVarPackage != null)
            {
                // VAR name already exists - UNIQUE constraint ensures only one record
                if (existingVarPackage.RepositoryId == repositoryId)
                {
                    // Same repository - check if same file path
                    if (string.Equals(existingVarPackage.FilePath, varFile, StringComparison.OrdinalIgnoreCase))
                    {
                        // Same file - already indexed, skip organization
                        result.SkippedFiles.Add(varFile);
                        result.Log($"Skipped already indexed file: {Path.GetFileName(varFile)}");
                        continue;
                    }
                    else
                    {
                        // Different file path but same VAR name in same repository
                        // This shouldn't happen, but handle it by moving to redundant
                        await MoveToRedundantAsync(
                            varFile,
                            redundantDir,
                            result,
                            $"VAR name {varName} already indexed from different location in this repository",
                            cancellationToken);
                        continue;
                    }
                }
                else
                {
                    // Different repository - VAR name already registered from another repository
                    // Since VAR name is unique globally, we log this as conflict
                    var existingRepo = await _unitOfWork.Repositories.GetByIdAsync(
                        existingVarPackage.RepositoryId,
                        cancellationToken);
                    
                    result.CrossRepositoryConflicts.Add(new CrossRepositoryConflictInfo
                    {
                        VarName = varName,
                        ExistingRepositoryId = existingVarPackage.RepositoryId,
                        ExistingRepositoryName = existingRepo?.Name ?? $"Repository {existingVarPackage.RepositoryId}",
                        ExistingFilePath = existingVarPackage.FilePath,
                        NewRepositoryId = repositoryId,
                        NewFilePath = varFile,
                        Warning = $"VAR {varName} already exists in repository {existingRepo?.Name ?? existingVarPackage.RepositoryId.ToString()}. Current file will not be indexed (UNIQUE constraint)."
                    });
                    
                    result.SkippedFiles.Add(varFile);
                    result.Log($"Skipped cross-repository conflict: {varName} - already exists in repository {existingVarPackage.RepositoryId}");
                    continue;
                }
            }
            
            // Determine destination path within this repository
            var creatorDir = Path.Combine(tidyDir, parsedName.Creator);
            Directory.CreateDirectory(creatorDir);
            
            var destinationPath = Path.Combine(creatorDir, Path.GetFileName(varFile));
            
            // Check for local duplicate (same repository)
            if (File.Exists(destinationPath))
            {
                // Check if files are identical
                var existingHash = await _fileHashService.ComputeFileHashAsync(destinationPath, cancellationToken);
                var fileHash = await _fileHashService.ComputeFileHashAsync(varFile, cancellationToken);
                
                if (existingHash == fileHash)
                {
                    // Same file content - move source to redundant
                    await MoveToRedundantAsync(
                        varFile,
                        redundantDir,
                        result,
                        "Identical file already exists in tidy directory",
                        cancellationToken);
                }
                else
                {
                    // Different file with same name - this should not happen after validation
                    // But handle it by renaming
                    result.Log($"Warning: File {varFile} has same name as existing but different content. Renaming.");
                    destinationPath = GenerateUniqueFileName(destinationPath);
                    await MoveFileAsync(varFile, destinationPath, result, cancellationToken);
                }
            }
            else
            {
                // No local duplicate, move to tidy directory
                await MoveFileAsync(varFile, destinationPath, result, cancellationToken);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Moves an invalid file to the invalid directory.
    /// </summary>
    private async Task MoveToInvalidAsync(
        string filePath,
        string invalidDir,
        OrganizationResult result,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var destination = Path.Combine(invalidDir, fileName);
                destination = GenerateUniqueFileName(destination);
                
                File.Move(filePath, destination);
                result.InvalidFiles.Add(filePath);
                result.Log($"Moved invalid file: {fileName}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to move invalid file {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Moves a redundant file to the redundant directory.
    /// </summary>
    private async Task MoveToRedundantAsync(
        string filePath,
        string redundantDir,
        OrganizationResult result,
        string reason,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var destination = Path.Combine(redundantDir, fileName);
                destination = GenerateUniqueFileName(destination);
                
                File.Move(filePath, destination);
                result.RedundantFiles.Add(filePath);
                result.Log($"Moved redundant file: {fileName} - {reason}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to move redundant file {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Moves a file to a destination path.
    /// </summary>
    private async Task MoveFileAsync(
        string sourcePath,
        string destinationPath,
        OrganizationResult result,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                File.Move(sourcePath, destinationPath);
                result.MovedFiles.Add(sourcePath);
                result.Log($"Moved file: {Path.GetFileName(sourcePath)} -> {Path.GetRelativePath(Path.GetDirectoryName(sourcePath)!, destinationPath)}");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to move file {Path.GetFileName(sourcePath)}: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Generates a unique filename by appending a counter if the file already exists.
    /// </summary>
    /// <param name="filePath">Original file path</param>
    /// <returns>Unique file path</returns>
    public static string GenerateUniqueFileName(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return filePath;
        }
        
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        
        var counter = 1;
        while (true)
        {
            var newFileName = $"{fileNameWithoutExt}({counter}){extension}";
            var newPath = Path.Combine(directory, newFileName);
            
            if (!File.Exists(newPath))
            {
                return newPath;
            }
            
            counter++;
            
            // Safety check to avoid infinite loop
            if (counter > 10000)
            {
                throw new InvalidOperationException($"Cannot generate unique filename for {filePath} after 10000 attempts");
            }
        }
    }
}

