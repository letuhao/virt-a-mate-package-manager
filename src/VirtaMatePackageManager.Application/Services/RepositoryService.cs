using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.Repository;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for repository management operations.
/// </summary>
public class RepositoryService : IRepositoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly RepositoryScanningService _scanningService;
    private readonly IVarPackageService _varPackageService;
    private readonly Core.Services.Parsing.VarFileParsingService _parsingService;
    private readonly Core.Services.Validation.VarFileValidationService _validationService;
    private readonly Core.Services.FileSystem.FileHashService _fileHashService;

    public RepositoryService(
        IUnitOfWork unitOfWork,
        IVarPackageService varPackageService,
        RepositoryScanningService? scanningService = null,
        Core.Services.Parsing.VarFileParsingService? parsingService = null,
        Core.Services.Validation.VarFileValidationService? validationService = null,
        Core.Services.FileSystem.FileHashService? fileHashService = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _varPackageService = varPackageService ?? throw new ArgumentNullException(nameof(varPackageService));
        _scanningService = scanningService ?? new RepositoryScanningService();
        _parsingService = parsingService ?? new Core.Services.Parsing.VarFileParsingService();
        _validationService = validationService ?? new Core.Services.Validation.VarFileValidationService();
        _fileHashService = fileHashService ?? new Core.Services.FileSystem.FileHashService();
    }

    public async Task<Result<int>> AddRepositoryAsync(
        AddRepositoryCommand command,
        CancellationToken ct)
    {
        // Validate path
        if (string.IsNullOrWhiteSpace(command.Path))
        {
            return Result<int>.Failure(
                "Repository path cannot be empty",
                ErrorCode.InvalidInput);
        }

        if (!Directory.Exists(command.Path))
        {
            return Result<int>.Failure(
                $"Repository path does not exist: {command.Path}",
                ErrorCode.DirectoryNotFound);
        }

        // Check if repository with same path already exists
        var existingRepo = await _unitOfWork.Repositories.GetByPathAsync(command.Path, ct);
        if (existingRepo != null)
        {
            return Result<int>.Failure(
                $"Repository with path '{command.Path}' already exists",
                ErrorCode.RepositoryAlreadyExists);
        }

        // Create new repository
        var repository = new Repository
        {
            Name = command.Name,
            Path = Path.GetFullPath(command.Path),
            Description = command.Description,
            Priority = command.Priority,
            Enabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repositories.AddAsync(repository, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<int>.Success(repository.Id);
    }

    public async Task<Result> UpdateRepositoryAsync(
        UpdateRepositoryCommand command,
        CancellationToken ct)
    {
        var repository = await _unitOfWork.Repositories.GetByIdAsync(command.Id, ct);
        if (repository == null)
        {
            return Result.Failure(
                $"Repository {command.Id} not found",
                ErrorCode.RepositoryNotFound);
        }

        // Update fields
        if (command.Name != null)
        {
            repository.Name = command.Name;
        }

        if (command.Path != null)
        {
            if (!Directory.Exists(command.Path))
            {
                return Result.Failure(
                    $"Repository path does not exist: {command.Path}",
                    ErrorCode.DirectoryNotFound);
            }

            // Check if another repository with same path exists
            var existingRepo = await _unitOfWork.Repositories.GetByPathAsync(command.Path, ct);
            if (existingRepo != null && existingRepo.Id != command.Id)
            {
                return Result.Failure(
                    $"Repository with path '{command.Path}' already exists",
                    ErrorCode.RepositoryAlreadyExists);
            }

            repository.Path = Path.GetFullPath(command.Path);
        }

        if (command.Description != null)
        {
            repository.Description = command.Description;
        }

        if (command.Priority.HasValue)
        {
            repository.Priority = command.Priority.Value;
        }

        repository.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repositories.UpdateAsync(repository, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteRepositoryAsync(
        int repositoryId,
        CancellationToken ct)
    {
        var repository = await _unitOfWork.Repositories.GetByIdAsync(repositoryId, ct);
        if (repository == null)
        {
            return Result.Failure(
                $"Repository {repositoryId} not found",
                ErrorCode.RepositoryNotFound);
        }

        // Check if repository has VAR packages
        var varPackages = await _unitOfWork.VarPackages.GetByRepositoryIdAsync(repositoryId, ct);
        if (varPackages.Any())
        {
            return Result.Failure(
                $"Cannot delete repository {repositoryId}: it contains {varPackages.Count()} VAR packages. Remove VAR packages first.",
                ErrorCode.RepositoryAccessDenied);
        }

        await _unitOfWork.Repositories.DeleteAsync(repositoryId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> EnableRepositoryAsync(
        int repositoryId,
        bool enabled,
        CancellationToken ct)
    {
        var repository = await _unitOfWork.Repositories.GetByIdAsync(repositoryId, ct);
        if (repository == null)
        {
            return Result.Failure(
                $"Repository {repositoryId} not found",
                ErrorCode.RepositoryNotFound);
        }

        repository.Enabled = enabled;
        repository.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Repositories.UpdateAsync(repository, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<IEnumerable<RepositoryDto>>> GetAllRepositoriesAsync(
        CancellationToken ct)
    {
        var repositories = await _unitOfWork.Repositories.GetAllAsync(ct);
        
        var dtos = repositories.Select(r => new RepositoryDto(
            r.Id,
            r.Name,
            r.Path,
            r.Description,
            r.Priority,
            r.Enabled,
            r.CreatedAt,
            r.UpdatedAt
        ));

        return Result<IEnumerable<RepositoryDto>>.Success(dtos);
    }

    public async Task<Result<RepositoryDto>> GetRepositoryByIdAsync(
        int id,
        CancellationToken ct)
    {
        var repository = await _unitOfWork.Repositories.GetByIdAsync(id, ct);
        if (repository == null)
        {
            return Result<RepositoryDto>.Failure(
                $"Repository {id} not found",
                ErrorCode.RepositoryNotFound);
        }

        var dto = new RepositoryDto(
            repository.Id,
            repository.Name,
            repository.Path,
            repository.Description,
            repository.Priority,
            repository.Enabled,
            repository.CreatedAt,
            repository.UpdatedAt
        );

        return Result<RepositoryDto>.Success(dto);
    }

    public async Task<Result<IEnumerable<RepositoryDto>>> GetEnabledRepositoriesAsync(
        CancellationToken ct)
    {
        var repositories = await _unitOfWork.Repositories.GetEnabledRepositoriesAsync(ct);
        
        var dtos = repositories.Select(r => new RepositoryDto(
            r.Id,
            r.Name,
            r.Path,
            r.Description,
            r.Priority,
            r.Enabled,
            r.CreatedAt,
            r.UpdatedAt
        ));

        return Result<IEnumerable<RepositoryDto>>.Success(dtos);
    }

    public async Task<Result<ScanResultDto>> ScanRepositoryAsync(
        int repositoryId,
        CancellationToken ct)
    {
        return await ScanRepositoryAsync(repositoryId, progress: null, ct);
    }

    public async Task<Result<ScanResultDto>> ScanRepositoryAsync(
        int repositoryId,
        IProgress<ScanProgressInfo>? progress,
        CancellationToken ct)
    {
        var repository = await _unitOfWork.Repositories.GetByIdAsync(repositoryId, ct);
        if (repository == null)
        {
            return Result<ScanResultDto>.Failure(
                $"Repository {repositoryId} not found",
                ErrorCode.RepositoryNotFound);
        }

        if (!Directory.Exists(repository.Path))
        {
            return Result<ScanResultDto>.Failure(
                $"Repository path does not exist: {repository.Path}",
                ErrorCode.DirectoryNotFound);
        }

        var startTime = DateTime.UtcNow;
        var filesAdded = 0;
        var filesUpdated = 0;
        var filesDeleted = 0;
        var errorsCount = 0;
        
        try
        {
            // Scan file system for VAR files
            var scannedFiles = _scanningService.ScanFileSystemForVarFiles(
                repository.Path,
                options: null,
                cancellationToken: ct);

            // Get existing VAR packages from database for this repository
            var existingVarPackages = (await _unitOfWork.VarPackages.GetByRepositoryIdAsync(repositoryId, ct))
                .ToList();
            
            var existingVarNamesByPath = existingVarPackages
                .ToDictionary(v => v.FilePath, v => v);
            
            var existingVarNames = existingVarPackages
                .Select(v => v.VarName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Process each scanned file
            var scannedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalFiles = scannedFiles.Count;
            var processedFiles = 0;
            
            // Report initial progress
            progress?.Report(new ScanProgressInfo(
                FilesScanned: 0,
                TotalFiles: totalFiles,
                FilesAdded: 0,
                FilesUpdated: 0,
                FilesDeleted: 0,
                ErrorsCount: 0,
                CurrentFile: "Starting scan...",
                ProgressPercentage: 0.0
            ));
            
            foreach (var scannedFile in scannedFiles)
            {
                ct.ThrowIfCancellationRequested();
                
                processedFiles++;
                scannedFilePaths.Add(scannedFile.Path);
                
                var fileName = Path.GetFileName(scannedFile.Path);
                
                // Report progress after each file
                var progressPercent = totalFiles > 0 ? (double)processedFiles / totalFiles * 100.0 : 0.0;
                progress?.Report(new ScanProgressInfo(
                    FilesScanned: processedFiles,
                    TotalFiles: totalFiles,
                    FilesAdded: filesAdded,
                    FilesUpdated: filesUpdated,
                    FilesDeleted: filesDeleted,
                    ErrorsCount: errorsCount,
                    CurrentFile: fileName,
                    ProgressPercentage: progressPercent
                ));
                
                try
                {
                    // Validate VAR filename format
                    var filenameValidation = _validationService.ValidateVarFileName(scannedFile.Path);
                    if (!filenameValidation.IsValid)
                    {
                        errorsCount++;
                        System.Diagnostics.Debug.WriteLine($"Invalid VAR filename {scannedFile.Path}: {filenameValidation.Error}");
                        continue;
                    }

                    var parsedName = filenameValidation.ParsedName!;
                    var varName = $"{parsedName.Creator}.{parsedName.Package}.{parsedName.Version}.var";

                    // Check if VAR name already exists in database (unique key)
                    var existingVarPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, ct);

                    if (existingVarPackage != null)
                    {
                        // VAR name exists - check if it's from this repository or different one
                        if (existingVarPackage.RepositoryId == repositoryId)
                        {
                            // Same repository - check if file was modified
                            if (existingVarPackage.FileModifiedAt == null || 
                                scannedFile.LastWriteTime > existingVarPackage.FileModifiedAt.Value)
                            {
                                // File was modified or metadata missing - refresh it
                                var refreshResult = await _varPackageService.RefreshVarPackageAsync(existingVarPackage.Id, ct);
                                if (refreshResult.IsSuccess)
                                {
                                    filesUpdated++;
                                }
                                else
                                {
                                    errorsCount++;
                                }
                            }
                            // else: file unchanged, skip
                        }
                        else
                        {
                            // VAR exists from different repository - check repository priority
                            var existingRepo = await _unitOfWork.Repositories.GetByIdAsync(existingVarPackage.RepositoryId, ct);
                            if (existingRepo != null && repository.Priority > existingRepo.Priority)
                            {
                                // Current repository has higher priority - update existing VAR package to use this file
                                try
                                {
                                    // Update file path and repository
                                    existingVarPackage.RepositoryId = repositoryId;
                                    existingVarPackage.FilePath = Path.GetFullPath(scannedFile.Path);
                                    existingVarPackage.RelativePath = Path.GetRelativePath(repository.Path, scannedFile.Path);
                                    
                                    // Update file info
                                    var fileInfo = new FileInfo(scannedFile.Path);
                                    existingVarPackage.FileSize = fileInfo.Length;
                                    existingVarPackage.FileModifiedAt = fileInfo.LastWriteTimeUtc;
                                    existingVarPackage.FileCreatedAt = fileInfo.CreationTimeUtc;
                                    
                                    // Re-extract metadata
                                    try
                                    {
                                        var metadata = _parsingService.ExtractVarMetadata(scannedFile.Path);
                                        existingVarPackage.LicenseType = metadata.LicenseType;
                                        existingVarPackage.Description = metadata.Description;
                                        existingVarPackage.Credits = metadata.Credits;
                                        existingVarPackage.Instructions = metadata.Instructions;
                                        existingVarPackage.PromotionalLink = metadata.PromotionalLink;
                                        existingVarPackage.ProgramVersion = metadata.ProgramVersion;
                                    }
                                    catch
                                    {
                                        // Metadata extraction failed, but continue
                                    }
                                    
                                    // Recompute hash
                                    try
                                    {
                                        existingVarPackage.FileHash = await _fileHashService.ComputeFileHashAsync(scannedFile.Path, ct);
                                    }
                                    catch
                                    {
                                        // Hash computation failed, but continue
                                    }
                                    
                                    existingVarPackage.LastScannedAt = DateTime.UtcNow;
                                    existingVarPackage.UpdatedAt = DateTime.UtcNow;
                                    
                                    await _unitOfWork.VarPackages.UpdateAsync(existingVarPackage, ct);
                                    filesUpdated++;
                                }
                                catch (Exception ex)
                                {
                                    errorsCount++;
                                    System.Diagnostics.Debug.WriteLine($"Failed to update VAR package for {varName}: {ex.Message}");
                                }
                            }
                            // else: existing repository has higher or equal priority, skip
                        }
                    }
                    else
                    {
                        // New VAR package - add it
                        var addResult = await _varPackageService.AddVarPackageAsync(
                            new AddVarPackageCommand(
                                RepositoryId: repositoryId,
                                FilePath: scannedFile.Path,
                                ExtractMetadata: true,
                                ExtractPreview: false), // Don't extract preview during scan
                            ct);
                        
                        if (addResult.IsSuccess)
                        {
                            filesAdded++;
                        }
                        else
                        {
                            errorsCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorsCount++;
                    System.Diagnostics.Debug.WriteLine($"Error processing file {scannedFile.Path}: {ex.Message}");
                }
            }

            // Report progress before checking deleted files
            progress?.Report(new ScanProgressInfo(
                FilesScanned: totalFiles,
                TotalFiles: totalFiles,
                FilesAdded: filesAdded,
                FilesUpdated: filesUpdated,
                FilesDeleted: filesDeleted,
                ErrorsCount: errorsCount,
                CurrentFile: "Checking for deleted files...",
                ProgressPercentage: 90.0 // Approximate - checking deleted files is quick
            ));
            
            // Find VAR packages in database that no longer exist in file system (deleted files)
            foreach (var existingVar in existingVarPackages)
            {
                ct.ThrowIfCancellationRequested();
                
                if (!scannedFilePaths.Contains(existingVar.FilePath))
                {
                    // File no longer exists - check if VAR has installations
                    var installations = await _unitOfWork.Installations.GetByVarPackageIdAsync(existingVar.Id, ct);
                    if (installations.Any())
                    {
                        // Has installations - just log warning, don't delete
                        System.Diagnostics.Debug.WriteLine($"VAR package {existingVar.VarName} file deleted but has installations");
                        errorsCount++;
                    }
                    else
                    {
                        // No installations - safe to delete
                        await _unitOfWork.VarPackages.DeleteAsync(existingVar.Id, ct);
                        filesDeleted++;
                    }
                }
            }

            // Save all changes
            await _unitOfWork.SaveChangesAsync(ct);

            var duration = DateTime.UtcNow - startTime;
            
            // Report final progress
            progress?.Report(new ScanProgressInfo(
                FilesScanned: totalFiles,
                TotalFiles: totalFiles,
                FilesAdded: filesAdded,
                FilesUpdated: filesUpdated,
                FilesDeleted: filesDeleted,
                ErrorsCount: errorsCount,
                CurrentFile: "Scan completed",
                ProgressPercentage: 100.0
            ));
            
            var result = new ScanResultDto(
                RepositoryId: repositoryId,
                FilesScanned: scannedFiles.Count,
                FilesAdded: filesAdded,
                FilesUpdated: filesUpdated,
                FilesDeleted: filesDeleted,
                ErrorsCount: errorsCount,
                Duration: duration,
                Status: ScanStatus.Completed
            );

            return Result<ScanResultDto>.Success(result);
        }
        catch (OperationCanceledException)
        {
            var duration = DateTime.UtcNow - startTime;
            return Result<ScanResultDto>.Success(new ScanResultDto(
                repositoryId,
                0,
                0,
                0,
                0,
                0,
                duration,
                ScanStatus.Cancelled
            ));
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            return Result<ScanResultDto>.Failure(
                $"Scan failed: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<ScanResultDto>> ScanAllRepositoriesAsync(
        CancellationToken ct)
    {
        return await ScanAllRepositoriesAsync(progress: null, ct);
    }

    public async Task<Result<ScanResultDto>> ScanAllRepositoriesAsync(
        IProgress<ScanProgressInfo>? progress,
        CancellationToken ct)
    {
        var repositories = await _unitOfWork.Repositories.GetEnabledRepositoriesAsync(ct);
        var repositoriesList = repositories.ToList();

        if (!repositoriesList.Any())
        {
            return Result<ScanResultDto>.Failure(
                "No enabled repositories found",
                ErrorCode.RepositoryNotFound);
        }

        var startTime = DateTime.UtcNow;
        var totalFilesScanned = 0;
        var totalFilesAdded = 0;
        var totalFilesUpdated = 0;
        var totalFilesDeleted = 0;
        var totalErrors = 0;

        var repositoryIndex = 0;
        foreach (var repository in repositoriesList)
        {
            ct.ThrowIfCancellationRequested();

            // Report progress for repository-level scanning
            if (progress != null)
            {
                progress.Report(new ScanProgressInfo(
                    FilesScanned: totalFilesScanned,
                    TotalFiles: 0, // Unknown total
                    FilesAdded: totalFilesAdded,
                    FilesUpdated: totalFilesUpdated,
                    FilesDeleted: totalFilesDeleted,
                    ErrorsCount: totalErrors,
                    CurrentFile: $"Scanning repository {repositoryIndex + 1}/{repositoriesList.Count}: {repository.Name}",
                    ProgressPercentage: (double)repositoryIndex / repositoriesList.Count * 100.0
                ));
            }

            var scanResult = await ScanRepositoryAsync(repository.Id, progress, ct);
            if (scanResult.IsSuccess && scanResult.Value != null)
            {
                totalFilesScanned += scanResult.Value.FilesScanned;
                totalFilesAdded += scanResult.Value.FilesAdded;
                totalFilesUpdated += scanResult.Value.FilesUpdated;
                totalFilesDeleted += scanResult.Value.FilesDeleted;
                totalErrors += scanResult.Value.ErrorsCount;
            }
            else
            {
                totalErrors++;
            }
            
            repositoryIndex++;
        }

        var duration = DateTime.UtcNow - startTime;
        var status = ct.IsCancellationRequested ? ScanStatus.Cancelled : ScanStatus.Completed;

        // Return aggregate result (RepositoryId = 0 means all repositories)
        var result = new ScanResultDto(
            RepositoryId: 0,
            FilesScanned: totalFilesScanned,
            FilesAdded: totalFilesAdded,
            FilesUpdated: totalFilesUpdated,
            FilesDeleted: totalFilesDeleted,
            ErrorsCount: totalErrors,
            Duration: duration,
            Status: status
        );

        return Result<ScanResultDto>.Success(result);
    }
}

