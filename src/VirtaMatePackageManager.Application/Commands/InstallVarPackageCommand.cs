namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to install a VAR package.
/// </summary>
public record InstallVarPackageCommand(
    int VarPackageId,
    int InstallationTargetId,
    bool InstallDependencies = false,
    bool SkipIfAlreadyInstalled = true
);

