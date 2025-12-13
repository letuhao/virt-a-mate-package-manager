namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Search statistics information.
/// </summary>
public record SearchStatisticsDto(
    int TotalVarPackages,
    int TotalRepositories,
    int TotalInstallations,
    int UniqueCreators,
    long TotalFileSize,
    Dictionary<string, int> VarPackagesByCreator,
    Dictionary<string, int> VarPackagesByLicenseType
);

