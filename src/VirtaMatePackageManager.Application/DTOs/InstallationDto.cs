namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Data transfer object for Installation entity.
/// </summary>
public record InstallationDto(
    int Id,
    int VarPackageId,
    string VarName,
    int InstallationTargetId,
    string InstallationTargetName,
    string SymlinkPath,
    bool IsEnabled,
    DateTime InstalledAt,
    string? InstalledBy
);

/// <summary>
/// Result of symlink verification.
/// </summary>
public record SymlinkVerificationResult(
    int InstallationId,
    bool Exists,
    bool IsValid,
    string? ErrorMessage
);

