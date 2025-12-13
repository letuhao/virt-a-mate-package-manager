using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for managing VAR package dependencies.
/// </summary>
public interface IDependencyService
{
    /// <summary>
    /// Resolves all unresolved dependencies across all VAR packages.
    /// </summary>
    Task<Result> ResolveDependenciesAsync(CancellationToken ct);

    /// <summary>
    /// Gets all dependencies for a VAR package.
    /// </summary>
    Task<Result<IEnumerable<DependencyDto>>> GetDependenciesAsync(
        int varPackageId,
        CancellationToken ct);

    /// <summary>
    /// Gets all VAR packages that depend on the specified VAR package (reverse dependencies).
    /// </summary>
    Task<Result<IEnumerable<VarPackageDto>>> GetReverseDependenciesAsync(
        int varPackageId,
        CancellationToken ct);

    /// <summary>
    /// Validates dependencies for a VAR package and returns resolution status.
    /// </summary>
    Task<Result<DependencyValidationResult>> ValidateDependenciesAsync(
        int varPackageId,
        CancellationToken ct);
}

