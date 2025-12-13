using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Queries;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service interface for searching VAR packages and retrieving search-related information.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Searches VAR packages with pagination.
    /// </summary>
    Task<Result<PagedResult<VarPackageDto>>> SearchAsync(
        VarSearchQuery query,
        CancellationToken ct);

    /// <summary>
    /// Gets all unique creator names.
    /// </summary>
    Task<Result<IEnumerable<string>>> GetCreatorsAsync(CancellationToken ct);

    /// <summary>
    /// Gets all package names for a specific creator.
    /// </summary>
    Task<Result<IEnumerable<string>>> GetPackagesByCreatorAsync(
        string creatorName,
        CancellationToken ct);

    /// <summary>
    /// Gets search statistics (total packages, creators, etc.).
    /// </summary>
    Task<Result<SearchStatisticsDto>> GetSearchStatisticsAsync(CancellationToken ct);
}

