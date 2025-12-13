using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for managing VAR package dependencies.
/// </summary>
public class DependencyService : IDependencyService
{
    private readonly IUnitOfWork _unitOfWork;

    public DependencyService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    public async Task<Result> ResolveDependenciesAsync(CancellationToken ct)
    {
        try
        {
            var unresolvedDependencies = (await _unitOfWork.Dependencies.GetAllAsync(ct))
                .Where(d => !d.IsResolved)
                .ToList();

            var resolvedCount = 0;

            foreach (var dependency in unresolvedDependencies)
            {
                // Try to find a VAR package that matches the dependency name
                // Dependency name format: "Creator.Package.Version.var" or "Creator.Package.latest.var"
                var varPackage = await ResolveDependencyNameAsync(dependency.DependencyName, ct);

                if (varPackage != null)
                {
                    dependency.IsResolved = true;
                    dependency.ResolvedVarPackageId = varPackage.Id;
                    await _unitOfWork.Dependencies.UpdateAsync(dependency, ct);
                    resolvedCount++;
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(
                $"Failed to resolve dependencies: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<IEnumerable<DependencyDto>>> GetDependenciesAsync(
        int varPackageId,
        CancellationToken ct)
    {
        try
        {
            var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
            if (varPackage == null)
            {
                return Result<IEnumerable<DependencyDto>>.Failure(
                    $"VAR package {varPackageId} not found",
                    ErrorCode.VarPackageNotFound);
            }

            var dependencies = (await _unitOfWork.Dependencies.GetAllAsync(ct))
                .Where(d => d.VarPackageId == varPackageId)
                .ToList();

            var dtos = new List<DependencyDto>();

            foreach (var dependency in dependencies)
            {
                string? resolvedVarName = null;
                if (dependency.IsResolved && dependency.ResolvedVarPackageId.HasValue)
                {
                    var resolvedVar = await _unitOfWork.VarPackages.GetByIdAsync(
                        dependency.ResolvedVarPackageId.Value,
                        ct);
                    resolvedVarName = resolvedVar?.VarName;
                }

                dtos.Add(new DependencyDto(
                    dependency.Id,
                    dependency.DependencyName,
                    dependency.VersionConstraint,
                    dependency.IsOptional,
                    dependency.IsResolved,
                    dependency.ResolvedVarPackageId,
                    resolvedVarName
                ));
            }

            return Result<IEnumerable<DependencyDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<DependencyDto>>.Failure(
                $"Failed to get dependencies: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<IEnumerable<VarPackageDto>>> GetReverseDependenciesAsync(
        int varPackageId,
        CancellationToken ct)
    {
        try
        {
            var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
            if (varPackage == null)
            {
                return Result<IEnumerable<VarPackageDto>>.Failure(
                    $"VAR package {varPackageId} not found",
                    ErrorCode.VarPackageNotFound);
            }

            // Find all dependencies that reference this VAR package
            var dependencies = (await _unitOfWork.Dependencies.GetAllAsync(ct))
                .Where(d => d.IsResolved && d.ResolvedVarPackageId == varPackageId)
                .ToList();

            var dependentVarPackageIds = dependencies
                .Select(d => d.VarPackageId)
                .Distinct()
                .ToList();

            var dependentVarPackages = new List<VarPackage>();
            foreach (var id in dependentVarPackageIds)
            {
                var depVarPackage = await _unitOfWork.VarPackages.GetByIdAsync(id, ct);
                if (depVarPackage != null)
                {
                    dependentVarPackages.Add(depVarPackage);
                }
            }

            // Convert to DTOs (simplified - would need full mapping)
            var dtos = dependentVarPackages.Select(v => new VarPackageDto(
                v.Id,
                v.VarName,
                v.CreatorName,
                v.PackageName,
                v.Version,
                v.FilePath,
                v.FileSize,
                v.FileHash,
                v.RepositoryId,
                string.Empty, // Repository name - would need to load
                v.LicenseType,
                v.Description,
                v.Credits,
                v.PreviewImagePath,
                v.FileModifiedAt,
                v.CreatedAt,
                v.UpdatedAt,
                null // Installation status - would need to load
            ));

            return Result<IEnumerable<VarPackageDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<VarPackageDto>>.Failure(
                $"Failed to get reverse dependencies: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<DependencyValidationResult>> ValidateDependenciesAsync(
        int varPackageId,
        CancellationToken ct)
    {
        try
        {
            var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, ct);
            if (varPackage == null)
            {
                return Result<DependencyValidationResult>.Failure(
                    $"VAR package {varPackageId} not found",
                    ErrorCode.VarPackageNotFound);
            }

            var dependencies = (await _unitOfWork.Dependencies.GetAllAsync(ct))
                .Where(d => d.VarPackageId == varPackageId)
                .ToList();

            var missingDependencies = new List<MissingDependencyDto>();
            var resolvedDependencies = new List<ResolvedDependencyDto>();

            foreach (var dependency in dependencies)
            {
                if (dependency.IsResolved && dependency.ResolvedVarPackageId.HasValue)
                {
                    var resolvedVar = await _unitOfWork.VarPackages.GetByIdAsync(
                        dependency.ResolvedVarPackageId.Value,
                        ct);

                    if (resolvedVar != null)
                    {
                        var repository = await _unitOfWork.Repositories.GetByIdAsync(
                            resolvedVar.RepositoryId,
                            ct);

                        resolvedDependencies.Add(new ResolvedDependencyDto(
                            dependency.DependencyName,
                            resolvedVar.Id,
                            resolvedVar.VarName,
                            repository?.Name ?? string.Empty
                        ));
                    }
                    else
                    {
                        // Mark as missing if resolved VAR was deleted
                        missingDependencies.Add(new MissingDependencyDto(
                            dependency.DependencyName,
                            dependency.VersionConstraint,
                            dependency.IsOptional
                        ));
                    }
                }
                else
                {
                    missingDependencies.Add(new MissingDependencyDto(
                        dependency.DependencyName,
                        dependency.VersionConstraint,
                        dependency.IsOptional
                    ));
                }
            }

            var allResolved = missingDependencies.Count == 0 ||
                missingDependencies.All(d => d.IsOptional);

            var result = new DependencyValidationResult(
                varPackageId,
                missingDependencies,
                resolvedDependencies,
                allResolved
            );

            return Result<DependencyValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<DependencyValidationResult>.Failure(
                $"Failed to validate dependencies: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    /// <summary>
    /// Resolves a dependency name to a VAR package.
    /// Handles both exact version matches and "latest" version requests.
    /// </summary>
    private async Task<VarPackage?> ResolveDependencyNameAsync(
        string dependencyName,
        CancellationToken ct)
    {
        // Normalize dependency name
        if (!dependencyName.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
        {
            dependencyName = dependencyName + ".var";
        }

        // Try exact match first
        var exactMatch = await _unitOfWork.VarPackages.GetByVarNameAsync(dependencyName, ct);
        if (exactMatch != null)
        {
            return exactMatch;
        }

        // If dependency name contains ".latest", find the latest version
        if (dependencyName.Contains(".latest", StringComparison.OrdinalIgnoreCase))
        {
            // Extract creator and package name
            // Format: "Creator.Package.latest.var"
            var parts = dependencyName.Split('.');
            if (parts.Length >= 3)
            {
                var creatorName = parts[0];
                var packageName = parts[1];

                // Get all VAR packages for this creator and package
                var varPackages = await _unitOfWork.VarPackages.GetByCreatorAndPackageAsync(
                    creatorName,
                    packageName,
                    ct);

                // Find the latest version (highest version number)
                var latest = varPackages
                    .Where(v => !string.IsNullOrWhiteSpace(v.Version))
                    .OrderByDescending(v => ParseVersion(v.Version))
                    .FirstOrDefault();

                return latest;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses a version string to a comparable value.
    /// Simple implementation - assumes numeric versions.
    /// </summary>
    private int ParseVersion(string version)
    {
        if (int.TryParse(version, out var result))
        {
            return result;
        }

        // Try to extract numeric part
        var numericPart = new string(version.TakeWhile(char.IsDigit).ToArray());
        if (int.TryParse(numericPart, out var numericResult))
        {
            return numericResult;
        }

        return 0;
    }
}

