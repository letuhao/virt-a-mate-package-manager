namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to add a new VAR package.
/// </summary>
public record AddVarPackageCommand(
    int RepositoryId,
    string FilePath,
    bool ExtractMetadata = true,
    bool ExtractPreview = true
);

