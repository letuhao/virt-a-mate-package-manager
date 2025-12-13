using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Queries;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for VAR package management operations.
/// </summary>
public interface IVarPackageService
{
    // Commands
    Task<Result<int>> AddVarPackageAsync(AddVarPackageCommand command, CancellationToken ct);
    Task<Result> UpdateVarPackageAsync(UpdateVarPackageCommand command, CancellationToken ct);
    Task<Result> DeleteVarPackageAsync(int varPackageId, CancellationToken ct);
    Task<Result> RefreshVarPackageAsync(int varPackageId, CancellationToken ct);
    
    // Queries
    Task<Result<VarPackageDto>> GetVarPackageByIdAsync(int id, CancellationToken ct);
    Task<Result<VarPackageDto>> GetVarPackageByNameAsync(string varName, CancellationToken ct);
    Task<Result<IEnumerable<VarPackageDto>>> SearchVarPackagesAsync(
        VarSearchQuery query,
        CancellationToken ct);
    Task<Result<PagedResult<VarPackageDto>>> GetVarPackagesPagedAsync(
        VarPagedQuery query,
        CancellationToken ct);
    
    // Operations
    Task<Result<VarPackageMetadata>> ExtractMetadataAsync(string varFilePath, CancellationToken ct);
    Task<Result<string>> ExtractPreviewImageAsync(int varPackageId, CancellationToken ct);
}

