namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to uninstall multiple VAR packages in batch.
/// </summary>
public record BatchUninstallCommand(
    IEnumerable<int> VarPackageIds,
    int InstallationTargetId
);

