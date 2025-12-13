namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Data transfer object for VarPackage entity.
/// </summary>
public record VarPackageDto(
    int Id,
    string VarName,
    string CreatorName,
    string PackageName,
    string Version,
    string FilePath,
    long FileSize,
    string? FileHash,
    int RepositoryId,
    string RepositoryName,
    string? LicenseType,
    string? Description,
    string? Credits,
    string? PreviewImagePath,
    DateTime? FileModifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    InstallationStatusDto? InstallationStatus
);

/// <summary>
/// Installation status information for a VAR package.
/// </summary>
public record InstallationStatusDto(
    int InstallationId,
    int InstallationTargetId,
    string InstallationTargetName,
    string SymlinkPath,
    bool IsEnabled,
    DateTime InstalledAt
);

/// <summary>
/// Paged result for paginated queries.
/// </summary>
public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

/// <summary>
/// VAR package metadata extracted from VAR file.
/// </summary>
public record VarPackageMetadata(
    string VarName,
    string CreatorName,
    string PackageName,
    string Version,
    long FileSize,
    string FileHash,
    string? LicenseType,
    string? Description,
    string? Credits,
    string? Instructions,
    string? PromotionalLink,
    string? ProgramVersion,
    IEnumerable<string> ContentList,
    Dictionary<string, DependencyInfo> Dependencies
);

/// <summary>
/// Dependency information.
/// </summary>
public record DependencyInfo(
    string DependencyName,
    string? LicenseType,
    Dictionary<string, DependencyInfo>? NestedDependencies
);

