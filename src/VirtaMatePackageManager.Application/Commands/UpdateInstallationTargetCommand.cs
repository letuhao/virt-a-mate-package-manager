namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to update an existing installation target.
/// </summary>
public record UpdateInstallationTargetCommand(
    int Id,
    string? Name,
    string? Path,
    string? ProfileName,
    string? Description
);

