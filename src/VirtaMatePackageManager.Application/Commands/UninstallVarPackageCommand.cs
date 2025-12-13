namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to uninstall a VAR package.
/// </summary>
public record UninstallVarPackageCommand(
    int VarPackageId,
    int InstallationTargetId,
    bool RemoveSymlink = true
);

