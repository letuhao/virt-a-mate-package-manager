using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for installation target management operations.
/// </summary>
public interface IInstallationTargetService
{
    // Commands
    Task<Result<int>> AddInstallationTargetAsync(AddInstallationTargetCommand command, CancellationToken ct);
    Task<Result> UpdateInstallationTargetAsync(UpdateInstallationTargetCommand command, CancellationToken ct);
    Task<Result> DeleteInstallationTargetAsync(int targetId, CancellationToken ct);
    Task<Result> SetActiveTargetAsync(int targetId, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<InstallationTargetDto>>> GetAllTargetsAsync(CancellationToken ct);
    Task<Result<InstallationTargetDto>> GetTargetByIdAsync(int id, CancellationToken ct);
    Task<Result<InstallationTargetDto?>> GetActiveTargetAsync(CancellationToken ct);
    Task<Result<IEnumerable<InstallationTargetDto>>> GetTargetsByProfileAsync(
        string profileName,
        CancellationToken ct);
}

