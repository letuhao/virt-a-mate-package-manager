using VirtaMatePackageManager.Application.Commands;
using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for repository management operations.
/// </summary>
public interface IRepositoryService
{
    // Commands
    Task<Result<int>> AddRepositoryAsync(AddRepositoryCommand command, CancellationToken ct);
    Task<Result> UpdateRepositoryAsync(UpdateRepositoryCommand command, CancellationToken ct);
    Task<Result> DeleteRepositoryAsync(int repositoryId, CancellationToken ct);
    Task<Result> EnableRepositoryAsync(int repositoryId, bool enabled, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<RepositoryDto>>> GetAllRepositoriesAsync(CancellationToken ct);
    Task<Result<RepositoryDto>> GetRepositoryByIdAsync(int id, CancellationToken ct);
    Task<Result<IEnumerable<RepositoryDto>>> GetEnabledRepositoriesAsync(CancellationToken ct);
    
    // Operations
    Task<Result<ScanResultDto>> ScanRepositoryAsync(int repositoryId, CancellationToken ct);
    Task<Result<ScanResultDto>> ScanRepositoryAsync(int repositoryId, IProgress<ScanProgressInfo>? progress, CancellationToken ct);
    Task<Result<ScanResultDto>> ScanAllRepositoriesAsync(CancellationToken ct);
    Task<Result<ScanResultDto>> ScanAllRepositoriesAsync(IProgress<ScanProgressInfo>? progress, CancellationToken ct);
}

