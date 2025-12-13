namespace VirtaMatePackageManager.Application.Queries;

/// <summary>
/// Query parameters for searching VAR packages.
/// </summary>
public record VarSearchQuery(
    string? CreatorName,
    string? PackageName,
    string? Version,
    string? LicenseType,
    int? RepositoryId,
    bool? IsInstalled,
    int? InstallationTargetId,
    string? SearchText,
    DateTime? CreatedAfter,
    DateTime? CreatedBefore,
    long? MinFileSize,
    long? MaxFileSize
)
{
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }
}

/// <summary>
/// Paged query for VAR packages.
/// </summary>
public record VarPagedQuery(
    int Page,
    int PageSize,
    VarSearchQuery? Filters,
    string? SortBy,
    bool SortDescending
);

