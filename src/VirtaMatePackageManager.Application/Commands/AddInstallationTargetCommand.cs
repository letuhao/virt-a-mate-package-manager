namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to add a new installation target.
/// </summary>
public record AddInstallationTargetCommand(
    string Name,
    string Path,
    string? ProfileName,
    string? Description
);

