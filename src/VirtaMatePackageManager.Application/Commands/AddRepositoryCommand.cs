namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to add a new repository.
/// </summary>
public record AddRepositoryCommand(
    string Name,
    string Path,
    string? Description,
    int Priority
);

