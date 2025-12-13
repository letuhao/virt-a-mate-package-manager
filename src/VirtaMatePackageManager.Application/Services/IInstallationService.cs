using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for VAR package installation operations.
/// </summary>
public interface IInstallationService
{
    // Commands
    Task<Result<int>> InstallVarPackageAsync(InstallVarPackageCommand command, CancellationToken ct);
    Task<Result> UninstallVarPackageAsync(UninstallVarPackageCommand command, CancellationToken ct);
    Task<Result> BatchInstallVarPackagesAsync(BatchInstallCommand command, CancellationToken ct);
    Task<Result> BatchUninstallVarPackagesAsync(BatchUninstallCommand command, CancellationToken ct);
    Task<Result> EnableInstallationAsync(int installationId, bool enabled, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByVarPackageAsync(
        int varPackageId,
        CancellationToken ct);
    Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByTargetAsync(
        int targetId,
        CancellationToken ct);
    Task<Result<InstallationDto>> GetInstallationAsync(int installationId, CancellationToken ct);
    
    // Operations
    Task<Result> VerifyInstallationsAsync(int targetId, CancellationToken ct);
    Task<Result<int>> RepairBrokenSymlinksAsync(int targetId, CancellationToken ct);
}

