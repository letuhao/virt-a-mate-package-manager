namespace VirtaMatePackageManager.Application.Commands;

/// <summary>
/// Command to update an existing repository.
/// </summary>
public record UpdateRepositoryCommand(
    int Id,
    string? Name,
    string? Path,
    string? Description,
    int? Priority
);

