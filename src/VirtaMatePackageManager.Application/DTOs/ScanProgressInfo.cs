namespace VirtaMatePackageManager.Application.DTOs;

/// <summary>
/// Progress information for repository scanning operations.
/// </summary>
public record ScanProgressInfo(
    int FilesScanned,
    int TotalFiles,
    int FilesAdded,
    int FilesUpdated,
    int FilesDeleted,
    int ErrorsCount,
    string? CurrentFile,
    double ProgressPercentage
);

