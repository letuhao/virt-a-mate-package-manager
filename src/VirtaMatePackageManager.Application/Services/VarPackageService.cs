using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Queries;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.Content;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.Services.Parsing;
using VirtaMatePackageManager.Core.Services.Preview;
using VirtaMatePackageManager.Core.Services.Validation;
using VirtaMatePackageManager.Core.ValueObjects;
using CoreDependencyInfo = VirtaMatePackageManager.Core.ValueObjects.Metadata.DependencyInfo;
using AppDependencyInfo = VirtaMatePackageManager.Application.DTOs.DependencyInfo;
using VarMetadata = VirtaMatePackageManager.Core.ValueObjects.Metadata.VarMetadata;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for VAR package management operations.
/// </summary>
public class VarPackageService : IVarPackageService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly VarFileValidationService _validationService;
    private readonly VarFileParsingService _parsingService;
    private readonly DependencyExtractionService _dependencyExtractionService;
    private readonly ContentAnalysisService _contentAnalysisService;
    private readonly FileHashService _fileHashService;
    private readonly PreviewImageExtractionService _previewExtractionService;

    public VarPackageService(
        IUnitOfWork unitOfWork,
        VarFileValidationService? validationService = null,
        VarFileParsingService? parsingService = null,
        DependencyExtractionService? dependencyExtractionService = null,
        ContentAnalysisService? contentAnalysisService = null,
        FileHashService? fileHashService = null,
        PreviewImageExtractionService? previewExtractionService = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _validationService = validationService ?? new VarFileValidationService();
        _parsingService = parsingService ?? new VarFileParsingService();
        _dependencyExtractionService = dependencyExtractionService ?? new DependencyExtractionService();
        _contentAnalysisService = contentAnalysisService ?? new ContentAnalysisService();
        _fileHashService = fileHashService ?? new FileHashService();
        _previewExtractionService = previewExtractionService ?? new PreviewImageExtractionService(_contentAnalysisService, _unitOfWork);
    }

    public async Task<Result<int>> AddVarPackageAsync(
        AddVarPackageCommand command,
        CancellationToken ct)
    {
        // Validate file exists
        if (!File.Exists(command.FilePath))
        {
            return Result<int>.Failure(
                $"VAR file not found: {command.FilePath}",
                ErrorCode.FileNotFound);
        }

        // Get repository
        var repository = await _unitOfWork.Repositories.GetByIdAsync(command.RepositoryId, ct);
        if (repository == null)
        {
            return Result<int>.Failure(
                $"Repository {command.RepositoryId} not found",
                ErrorCode.RepositoryNotFound);
        }

        // Validate VAR filename
        var filenameValidation = _validationService.ValidateVarFileName(command.FilePath);
        if (!filenameValidation.IsValid)
        {
            return Result<int>.Failure(
                $"Invalid VAR filename: {filenameValidation.Error}",
                ErrorCode.ValidationError);
        }

        var parsedName = filenameValidation.ParsedName!;
        var varName = parsedName.Creator + "." + parsedName.Package + "." + parsedName.Version + ".var";

        // Check if VAR name already exists (UNIQUE constraint)
        var existingVarPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, ct);
        if (existingVarPackage != null)
        {
            return Result<int>.Failure(
                $"VAR package with name '{varName}' already exists",
                ErrorCode.VarPackageAlreadyExists);
        }

        // Get file info
        var fileInfo = new FileInfo(command.FilePath);
        var fileSize = fileInfo.Length;
        var fileModifiedAt = fileInfo.LastWriteTimeUtc;
        var fileCreatedAt = fileInfo.CreationTimeUtc;

        // Compute file hash if requested
        string? fileHash = null;
        if (command.ExtractMetadata)
        {
            try
            {
                fileHash = await _fileHashService.ComputeFileHashAsync(command.FilePath, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - hash is optional
                System.Diagnostics.Debug.WriteLine($"Failed to compute hash: {ex.Message}");
            }
        }

        // Extract metadata if requested
        VarMetadata? metadata = null;
        if (command.ExtractMetadata)
        {
            try
            {
                metadata = _parsingService.ExtractVarMetadata(command.FilePath);
            }
            catch (Exception ex)
            {
                // Log but continue - metadata extraction failure shouldn't block adding
                System.Diagnostics.Debug.WriteLine($"Failed to extract metadata: {ex.Message}");
            }
        }

        // Create VAR package entity
        var varPackage = new VarPackage
        {
            RepositoryId = command.RepositoryId,
            VarName = varName,
            CreatorName = parsedName.Creator,
            PackageName = parsedName.Package,
            Version = parsedName.Version,
            FilePath = Path.GetFullPath(command.FilePath),
            FileSize = fileSize,
            FileHash = fileHash,
            RelativePath = Path.GetRelativePath(repository.Path, command.FilePath),
            LicenseType = metadata?.LicenseType,
            Description = metadata?.Description,
            Credits = metadata?.Credits,
            Instructions = metadata?.Instructions,
            PromotionalLink = metadata?.PromotionalLink,
            ProgramVersion = metadata?.ProgramVersion,
            FileCreatedAt = fileCreatedAt,
            FileModifiedAt = fileModifiedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.VarPackages.AddAsync(varPackage, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // Extract dependencies if metadata was extracted
        if (metadata != null && !string.IsNullOrEmpty(metadata.RawJson))
        {
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(metadata.RawJson);
                var dependencies = _dependencyExtractionService.ExtractDependencies(jsonDoc.RootElement);

                foreach (var dep in dependencies)
                {
                    var dependency = new Dependency
                    {
                        VarPackageId = varPackage.Id,
                        DependencyName = dep.Name,
                        VersionConstraint = dep.Name.Contains(".latest") ? "latest" : null,
                        IsOptional = dep.IsOptional, // Determined from metadata
                        IsResolved = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _unitOfWork.Dependencies.AddAsync(dependency, ct);
                }

                await _unitOfWork.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail
                System.Diagnostics.Debug.WriteLine($"Failed to extract dependencies: {ex.Message}");
            }
        }

        // Extract preview images if requested
        if (command.ExtractPreview)
        {
            try
            {
                // Get preview output directory (default to repository/previews, can be configured later)
                var previewOutputDir = Path.Combine(repository.Path, "previews");
                var previewImages = await _previewExtractionService.ExtractPreviewImagesAsync(
                    command.FilePath,
                    varPackage.Id,
                    previewOutputDir,
                    ct);

                // Update preview image path if extracted
                if (previewImages.Count > 0)
                {
                    // Use the first preview image as the primary preview
                    var firstPreview = previewImages[0];
                    varPackage.PreviewImagePath = firstPreview.PreviewPath;
                    await _unitOfWork.VarPackages.UpdateAsync(varPackage, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                // Log but don't fail
                System.Diagnostics.Debug.WriteLine($"Failed to extract preview images: {ex.Message}");
            }
        }

        return Result<int>.Success(varPackage.Id);
    }

    public async Task<Result> UpdateVarPackageAsync(
        UpdateVarPackageCommand command,
        CancellationToken ct)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(command.Id, ct);
        if (varPackage == null)
        {
            return Result.Failure(
                $"VAR package {command.Id} not found",
                ErrorCode.VarPackageNotFound);
        }

        // Update fields
        if (command.Description != null)
        {
            varPackage.Description = command.Description;
        }

        if (command.LicenseType != null)
        {
            varPackage.LicenseType = command.LicenseType;
        }

        varPackage.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.VarPackages.UpdateAsync(varPackage, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> DeleteVarPackageAsync(
        int varPackageId,
        CancellationToken ct)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
        if (varPackage == null)
        {
            return Result.Failure(
                $"VAR package {varPackageId} not found",
                ErrorCode.VarPackageNotFound);
        }

        // Check if package has installations
        var installations = await _unitOfWork.Installations.GetByVarPackageIdAsync(varPackageId, ct);
        if (installations.Any())
        {
            return Result.Failure(
                $"Cannot delete VAR package {varPackageId}: it has {installations.Count()} installations. Remove installations first.",
                ErrorCode.InstallationFailed);
        }

        await _unitOfWork.VarPackages.DeleteAsync(varPackageId, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> RefreshVarPackageAsync(
        int varPackageId,
        CancellationToken ct)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
        if (varPackage == null)
        {
            return Result.Failure(
                $"VAR package {varPackageId} not found",
                ErrorCode.VarPackageNotFound);
        }

        if (!File.Exists(varPackage.FilePath))
        {
            return Result.Failure(
                $"VAR file not found: {varPackage.FilePath}",
                ErrorCode.FileNotFound);
        }

        // Re-extract metadata
        try
        {
            var metadata = _parsingService.ExtractVarMetadata(varPackage.FilePath);
            
            // Update metadata fields
            varPackage.LicenseType = metadata.LicenseType;
            varPackage.Description = metadata.Description;
            varPackage.Credits = metadata.Credits;
            varPackage.Instructions = metadata.Instructions;
            varPackage.PromotionalLink = metadata.PromotionalLink;
            varPackage.ProgramVersion = metadata.ProgramVersion;

            // Update file info
            var fileInfo = new FileInfo(varPackage.FilePath);
            varPackage.FileSize = fileInfo.Length;
            varPackage.FileModifiedAt = fileInfo.LastWriteTimeUtc;

            // Recompute hash
            varPackage.FileHash = await _fileHashService.ComputeFileHashAsync(varPackage.FilePath, ct);

            varPackage.UpdatedAt = DateTime.UtcNow;
            varPackage.LastScannedAt = DateTime.UtcNow;

            await _unitOfWork.VarPackages.UpdateAsync(varPackage, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                $"Failed to refresh VAR package: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<VarPackageDto>> GetVarPackageByIdAsync(
        int id,
        CancellationToken ct)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(id, ct);
        if (varPackage == null)
        {
            return Result<VarPackageDto>.Failure(
                $"VAR package {id} not found",
                ErrorCode.VarPackageNotFound);
        }

        var dto = await MapVarPackageToDto(varPackage, ct);
        return Result<VarPackageDto>.Success(dto);
    }

    public async Task<Result<VarPackageDto>> GetVarPackageByNameAsync(
        string varName,
        CancellationToken ct)
    {
        // Ensure .var extension
        if (!varName.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
        {
            varName = varName + ".var";
        }

        var varPackage = await _unitOfWork.VarPackages.GetByVarNameAsync(varName, ct);
        if (varPackage == null)
        {
            return Result<VarPackageDto>.Failure(
                $"VAR package '{varName}' not found",
                ErrorCode.VarPackageNotFound);
        }

        var dto = await MapVarPackageToDto(varPackage, ct);
        return Result<VarPackageDto>.Success(dto);
    }

    public async Task<Result<IEnumerable<VarPackageDto>>> SearchVarPackagesAsync(
        VarSearchQuery query,
        CancellationToken ct)
    {
        var varPackages = await _unitOfWork.VarPackages.SearchAsync(
            creatorName: query.CreatorName,
            packageName: query.PackageName,
            version: query.Version,
            licenseType: query.LicenseType,
            repositoryId: query.RepositoryId,
            isInstalled: query.IsInstalled,
            cancellationToken: ct);

        // Apply additional filters
        var filtered = varPackages.AsQueryable();

        if (query.CreatedAfter.HasValue)
        {
            filtered = filtered.Where(v => v.CreatedAt >= query.CreatedAfter.Value);
        }

        if (query.CreatedBefore.HasValue)
        {
            filtered = filtered.Where(v => v.CreatedAt <= query.CreatedBefore.Value);
        }

        if (query.MinFileSize.HasValue)
        {
            filtered = filtered.Where(v => v.FileSize >= query.MinFileSize.Value);
        }

        if (query.MaxFileSize.HasValue)
        {
            filtered = filtered.Where(v => v.FileSize <= query.MaxFileSize.Value);
        }

        // Apply search text if provided
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var searchText = query.SearchText.ToLowerInvariant();
            filtered = filtered.Where(v =>
                v.CreatorName.ToLowerInvariant().Contains(searchText) ||
                v.PackageName.ToLowerInvariant().Contains(searchText) ||
                (v.Description != null && v.Description.ToLowerInvariant().Contains(searchText)));
        }

        // Apply sorting
        if (!string.IsNullOrWhiteSpace(query.SortBy))
        {
            filtered = query.SortBy.ToLowerInvariant() switch
            {
                "name" => query.SortDescending
                    ? filtered.OrderByDescending(v => v.VarName)
                    : filtered.OrderBy(v => v.VarName),
                "creator" => query.SortDescending
                    ? filtered.OrderByDescending(v => v.CreatorName)
                    : filtered.OrderBy(v => v.CreatorName),
                "size" => query.SortDescending
                    ? filtered.OrderByDescending(v => v.FileSize)
                    : filtered.OrderBy(v => v.FileSize),
                "created" => query.SortDescending
                    ? filtered.OrderByDescending(v => v.CreatedAt)
                    : filtered.OrderBy(v => v.CreatedAt),
                _ => filtered.OrderBy(v => v.VarName)
            };
        }
        else
        {
            filtered = filtered.OrderBy(v => v.VarName);
        }

        // Apply skip/take
        if (query.Skip.HasValue)
        {
            filtered = filtered.Skip(query.Skip.Value);
        }

        if (query.Take.HasValue)
        {
            filtered = filtered.Take(query.Take.Value);
        }

        var varPackagesList = filtered.ToList();
        var dtos = new List<VarPackageDto>();

        foreach (var varPackage in varPackagesList)
        {
            var dto = await MapVarPackageToDto(varPackage, ct);
            dtos.Add(dto);
        }

        return Result<IEnumerable<VarPackageDto>>.Success(dtos);
    }

    public async Task<Result<PagedResult<VarPackageDto>>> GetVarPackagesPagedAsync(
        VarPagedQuery query,
        CancellationToken ct)
    {
        // Build search query from paged query
        var searchQuery = new VarSearchQuery(
            query.Filters?.CreatorName,
            query.Filters?.PackageName,
            query.Filters?.Version,
            query.Filters?.LicenseType,
            query.Filters?.RepositoryId,
            query.Filters?.IsInstalled,
            query.Filters?.InstallationTargetId,
            query.Filters?.SearchText,
            query.Filters?.CreatedAfter,
            query.Filters?.CreatedBefore,
            query.Filters?.MinFileSize,
            query.Filters?.MaxFileSize)
        {
            SortBy = query.SortBy ?? query.Filters?.SortBy,
            SortDescending = query.SortDescending || (query.Filters?.SortDescending ?? false)
        };

        // Get total count (without pagination)
        var allResults = await SearchVarPackagesAsync(
            searchQuery with { Skip = null, Take = null },
            ct);

        if (!allResults.IsSuccess)
        {
            return Result<PagedResult<VarPackageDto>>.Failure(
                allResults.Error ?? "Search failed",
                allResults.ErrorCode ?? ErrorCode.Unknown);
        }

        var totalCount = allResults.Value!.Count();

        // Get paged results
        var pagedSearchQuery = searchQuery with
        {
            Skip = (query.Page - 1) * query.PageSize,
            Take = query.PageSize
        };

        var pagedResults = await SearchVarPackagesAsync(pagedSearchQuery, ct);

        if (!pagedResults.IsSuccess)
        {
            return Result<PagedResult<VarPackageDto>>.Failure(
                pagedResults.Error ?? "Search failed",
                pagedResults.ErrorCode ?? ErrorCode.Unknown);
        }

        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);

        var pagedResult = new PagedResult<VarPackageDto>(
            pagedResults.Value!,
            totalCount,
            query.Page,
            query.PageSize,
            totalPages
        );

        return Result<PagedResult<VarPackageDto>>.Success(pagedResult);
    }

    public async Task<Result<VarPackageMetadata>> ExtractMetadataAsync(
        string varFilePath,
        CancellationToken ct)
    {
        if (!File.Exists(varFilePath))
        {
            return Result<VarPackageMetadata>.Failure(
                $"VAR file not found: {varFilePath}",
                ErrorCode.FileNotFound);
        }

        try
        {
            // Validate filename
            var filenameValidation = _validationService.ValidateVarFileName(varFilePath);
            if (!filenameValidation.IsValid)
            {
                return Result<VarPackageMetadata>.Failure(
                    $"Invalid VAR filename: {filenameValidation.Error}",
                    ErrorCode.ValidationError);
            }

            var parsedName = filenameValidation.ParsedName!;

            // Extract metadata
            var metadata = _parsingService.ExtractVarMetadata(varFilePath);

            // Extract dependencies
            var dependenciesDict = new Dictionary<string, AppDependencyInfo>();
            if (metadata.HasDependencies && !string.IsNullOrEmpty(metadata.RawJson))
            {
                try
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(metadata.RawJson);
                    var deps = _dependencyExtractionService.ExtractDependencies(jsonDoc.RootElement);
                    
                    foreach (var dep in deps)
                    {
                        dependenciesDict[dep.Name] = new AppDependencyInfo(
                            dep.Name,
                            dep.LicenseType,
                            dep.NestedDependencies?.ToDictionary(
                                nd => nd.Name,
                                nd => new AppDependencyInfo(nd.Name, nd.LicenseType, null))
                        );
                    }
                }
                catch
                {
                    // Ignore dependency extraction errors
                }
            }

            // Get file info
            var fileInfo = new FileInfo(varFilePath);
            var fileHash = await _fileHashService.ComputeFileHashAsync(varFilePath, ct);

            var result = new VarPackageMetadata(
                VarName: parsedName.Creator + "." + parsedName.Package + "." + parsedName.Version,
                CreatorName: parsedName.Creator,
                PackageName: parsedName.Package,
                Version: parsedName.Version,
                FileSize: fileInfo.Length,
                FileHash: fileHash,
                LicenseType: metadata.LicenseType,
                Description: metadata.Description,
                Credits: metadata.Credits,
                Instructions: metadata.Instructions,
                PromotionalLink: metadata.PromotionalLink,
                ProgramVersion: metadata.ProgramVersion,
                ContentList: metadata.ContentList,
                Dependencies: dependenciesDict
            );

            return Result<VarPackageMetadata>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<VarPackageMetadata>.Failure(
                $"Failed to extract metadata: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<string>> ExtractPreviewImageAsync(
        int varPackageId,
        CancellationToken ct)
    {
        var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
        if (varPackage == null)
        {
            return Result<string>.Failure(
                $"VAR package {varPackageId} not found",
                ErrorCode.VarPackageNotFound);
        }

        if (!File.Exists(varPackage.FilePath))
        {
            return Result<string>.Failure(
                $"VAR file not found: {varPackage.FilePath}",
                ErrorCode.FileNotFound);
        }

        try
        {
            // Get repository for preview output directory
            var repository = await _unitOfWork.Repositories.GetByIdAsync(varPackage.RepositoryId, ct);
            if (repository == null)
            {
                return Result<string>.Failure(
                    $"Repository {varPackage.RepositoryId} not found",
                    ErrorCode.RepositoryNotFound);
            }

            var previewOutputDir = Path.Combine(repository.Path, "previews");
            Directory.CreateDirectory(previewOutputDir);

            var previewImages = await _previewExtractionService.ExtractPreviewImagesAsync(
                varPackage.FilePath,
                varPackageId,
                previewOutputDir,
                ct);

            if (previewImages.Count > 0)
            {
                // Update preview image path with first preview
                varPackage.PreviewImagePath = previewImages[0].PreviewPath;
                await _unitOfWork.VarPackages.UpdateAsync(varPackage, ct);
                await _unitOfWork.SaveChangesAsync(ct);

                return Result<string>.Success(previewImages[0].PreviewPath);
            }

            return Result<string>.Failure(
                "No preview images found in VAR file",
                ErrorCode.FileNotFound);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(
                $"Failed to extract preview image: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    private async Task<VarPackageDto> MapVarPackageToDto(
        VarPackage varPackage,
        CancellationToken ct)
    {
        // Load repository if not loaded
        if (varPackage.Repository == null)
        {
            varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackage.Id, ct) ?? varPackage;
        }

        var repository = varPackage.Repository ?? 
            await _unitOfWork.Repositories.GetByIdAsync(varPackage.RepositoryId, ct);

        // Get installation status (first active installation)
        InstallationStatusDto? installationStatus = null;
        var installations = await _unitOfWork.Installations.GetByVarPackageIdAsync(varPackage.Id, ct);
        var activeInstallation = installations.FirstOrDefault(i => i.IsEnabled);

        if (activeInstallation != null)
        {
            var target = activeInstallation.InstallationTarget ??
                await _unitOfWork.InstallationTargets.GetByIdAsync(activeInstallation.InstallationTargetId, ct);

            installationStatus = new InstallationStatusDto(
                activeInstallation.Id,
                activeInstallation.InstallationTargetId,
                target?.Name ?? string.Empty,
                activeInstallation.SymlinkPath,
                activeInstallation.IsEnabled,
                activeInstallation.InstalledAt
            );
        }

        return new VarPackageDto(
            varPackage.Id,
            varPackage.VarName,
            varPackage.CreatorName,
            varPackage.PackageName,
            varPackage.Version,
            varPackage.FilePath,
            varPackage.FileSize,
            varPackage.FileHash,
            varPackage.RepositoryId,
            repository?.Name ?? string.Empty,
            varPackage.LicenseType,
            varPackage.Description,
            varPackage.Credits,
            varPackage.PreviewImagePath,
            varPackage.FileModifiedAt,
            varPackage.CreatedAt,
            varPackage.UpdatedAt,
            installationStatus
        );
    }
}

