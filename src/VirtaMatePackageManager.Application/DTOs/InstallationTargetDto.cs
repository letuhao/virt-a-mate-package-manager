namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Data transfer object for InstallationTarget entity.
/// </summary>
public record InstallationTargetDto(
    int Id,
    string Name,
    string Path,
    string? ProfileName,
    string? Description,
    bool IsActive,
    int InstallationCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

