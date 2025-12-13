using VirtaMatePackageManager.Application.DTOs;
using VirtaMatePackageManager.Application.Queries;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Application.Services;

/// <summary>
/// Service implementation for searching VAR packages and retrieving search-related information.
/// </summary>
public class SearchService : ISearchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IVarPackageService _varPackageService;

    public SearchService(
        IUnitOfWork unitOfWork,
        IVarPackageService varPackageService)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _varPackageService = varPackageService ?? throw new ArgumentNullException(nameof(varPackageService));
    }

    public async Task<Result<PagedResult<VarPackageDto>>> SearchAsync(
        VarSearchQuery query,
        CancellationToken ct)
    {
        // Convert VarSearchQuery to VarPagedQuery for VarPackageService
        // For now, we'll use a default page size if not specified
        var pageSize = query.Take ?? 50;
        var page = query.Skip.HasValue ? (query.Skip.Value / pageSize) + 1 : 1;

        var pagedQuery = new VarPagedQuery(
            Page: page,
            PageSize: pageSize,
            Filters: query,
            SortBy: query.SortBy,
            SortDescending: query.SortDescending
        );

        return await _varPackageService.GetVarPackagesPagedAsync(pagedQuery, ct);
    }

    public async Task<Result<IEnumerable<string>>> GetCreatorsAsync(CancellationToken ct)
    {
        try
        {
            var varPackages = await _unitOfWork.VarPackages.GetAllAsync(ct);
            var creators = varPackages
                .Select(v => v.CreatorName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();

            return Result<IEnumerable<string>>.Success(creators);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<string>>.Failure(
                $"Failed to get creators: {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<IEnumerable<string>>> GetPackagesByCreatorAsync(
        string creatorName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(creatorName))
        {
            return Result<IEnumerable<string>>.Failure(
                "Creator name cannot be empty",
                ErrorCode.ValidationError);
        }

        try
        {
            var varPackages = await _unitOfWork.VarPackages.GetByCreatorAsync(creatorName, ct);
            var packages = varPackages
                .Select(v => v.PackageName)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p)
                .ToList();

            return Result<IEnumerable<string>>.Success(packages);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<string>>.Failure(
                $"Failed to get packages for creator '{creatorName}': {ex.Message}",
                ErrorCode.IOError);
        }
    }

    public async Task<Result<SearchStatisticsDto>> GetSearchStatisticsAsync(CancellationToken ct)
    {
        try
        {
            var varPackages = (await _unitOfWork.VarPackages.GetAllAsync(ct)).ToList();
            var repositories = (await _unitOfWork.Repositories.GetAllAsync(ct)).ToList();
            var installations = (await _unitOfWork.Installations.GetAllAsync(ct)).ToList();

            var totalVarPackages = varPackages.Count;
            var totalRepositories = repositories.Count;
            var totalInstallations = installations.Count;
            var uniqueCreators = varPackages
                .Select(v => v.CreatorName)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var totalFileSize = varPackages.Sum(v => v.FileSize);

            var varPackagesByCreator = varPackages
                .Where(v => !string.IsNullOrWhiteSpace(v.CreatorName))
                .GroupBy(v => v.CreatorName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count(),
                    StringComparer.OrdinalIgnoreCase);

            var varPackagesByLicenseType = varPackages
                .Where(v => !string.IsNullOrWhiteSpace(v.LicenseType))
                .GroupBy(v => v.LicenseType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key!,
                    g => g.Count(),
                    StringComparer.OrdinalIgnoreCase);

            var statistics = new SearchStatisticsDto(
                TotalVarPackages: totalVarPackages,
                TotalRepositories: totalRepositories,
                TotalInstallations: totalInstallations,
                UniqueCreators: uniqueCreators,
                TotalFileSize: totalFileSize,
                VarPackagesByCreator: varPackagesByCreator,
                VarPackagesByLicenseType: varPackagesByLicenseType
            );

            return Result<SearchStatisticsDto>.Success(statistics);
        }
        catch (Exception ex)
        {
            return Result<SearchStatisticsDto>.Failure(
                $"Failed to get search statistics: {ex.Message}",
                ErrorCode.IOError);
        }
    }
}

