namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to update an existing VAR package.
/// </summary>
public record UpdateVarPackageCommand(
    int Id,
    string? Description,
    string? LicenseType
);

