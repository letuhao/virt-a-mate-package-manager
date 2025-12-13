namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Data transfer object for Repository entity.
/// </summary>
public record RepositoryDto(
    int Id,
    string Name,
    string Path,
    string? Description,
    int Priority,
    bool Enabled,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

/// <summary>
/// Scan status for repository scanning operations.
/// </summary>
public enum ScanStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Result of scanning a repository.
/// </summary>
public record ScanResultDto(
    int RepositoryId,
    int FilesScanned,
    int FilesAdded,
    int FilesUpdated,
    int FilesDeleted,
    int ErrorsCount,
    TimeSpan Duration,
    ScanStatus Status
);

