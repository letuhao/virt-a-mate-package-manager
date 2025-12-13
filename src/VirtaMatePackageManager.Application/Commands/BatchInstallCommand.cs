namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to install multiple VAR packages in batch.
/// </summary>
public record BatchInstallCommand(
    IEnumerable<int> VarPackageIds,
    int InstallationTargetId,
    bool InstallDependencies = false
);

