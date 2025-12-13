# API Specifications

## Overview

This document defines the service interfaces, DTOs, and contracts for VirtaMatePackageManager. All interfaces follow CQRS pattern with clear separation of commands and queries.

## Service Layer Architecture

```
Application Layer Services:
├── IVarPackageService
├── IRepositoryService
├── IInstallationService
├── IDependencyService
├── ISearchService
└── IFileSystemService
```

---

## 1. Repository Service

### Interface Definition

```csharp
public interface IRepositoryService
{
    // Commands
    Task<Result<int>> AddRepositoryAsync(AddRepositoryCommand command, CancellationToken ct);
    Task<Result> UpdateRepositoryAsync(UpdateRepositoryCommand command, CancellationToken ct);
    Task<Result> DeleteRepositoryAsync(int repositoryId, CancellationToken ct);
    Task<Result> EnableRepositoryAsync(int repositoryId, bool enabled, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<RepositoryDto>>> GetAllRepositoriesAsync(CancellationToken ct);
    Task<Result<RepositoryDto>> GetRepositoryByIdAsync(int id, CancellationToken ct);
    Task<Result<IEnumerable<RepositoryDto>>> GetEnabledRepositoriesAsync(CancellationToken ct);
    
    // Operations
    Task<Result<ScanResult>> ScanRepositoryAsync(int repositoryId, CancellationToken ct);
    Task<Result<ScanResult>> ScanAllRepositoriesAsync(CancellationToken ct);
}
```

### DTOs

```csharp
public record AddRepositoryCommand(
    string Name,
    string Path,
    string? Description,
    int Priority
);

public record UpdateRepositoryCommand(
    int Id,
    string? Name,
    string? Path,
    string? Description,
    int? Priority
);

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

public record ScanResult(
    int RepositoryId,
    int FilesScanned,
    int FilesAdded,
    int FilesUpdated,
    int FilesDeleted,
    int ErrorsCount,
    TimeSpan Duration,
    ScanStatus Status
);

public enum ScanStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}
```

---

## 2. VAR Package Service

### Interface Definition

```csharp
public interface IVarPackageService
{
    // Commands
    Task<Result<int>> AddVarPackageAsync(AddVarPackageCommand command, CancellationToken ct);
    Task<Result> UpdateVarPackageAsync(UpdateVarPackageCommand command, CancellationToken ct);
    Task<Result> DeleteVarPackageAsync(int varPackageId, CancellationToken ct);
    Task<Result> RefreshVarPackageAsync(int varPackageId, CancellationToken ct);
    
    // Queries
    Task<Result<VarPackageDto>> GetVarPackageByIdAsync(int id, CancellationToken ct);
    Task<Result<VarPackageDto>> GetVarPackageByNameAsync(string varName, CancellationToken ct);
    Task<Result<IEnumerable<VarPackageDto>>> SearchVarPackagesAsync(
        VarSearchQuery query,
        CancellationToken ct);
    Task<Result<PagedResult<VarPackageDto>>> GetVarPackagesPagedAsync(
        VarPagedQuery query,
        CancellationToken ct);
    
    // Operations
    Task<Result<VarPackageMetadata>> ExtractMetadataAsync(string varFilePath, CancellationToken ct);
    Task<Result<string>> ExtractPreviewImageAsync(int varPackageId, CancellationToken ct);
}
```

### DTOs

```csharp
public record AddVarPackageCommand(
    int RepositoryId,
    string FilePath,
    bool ExtractMetadata = true,
    bool ExtractPreview = true
);

public record UpdateVarPackageCommand(
    int Id,
    string? Description,
    string? LicenseType
);

public record VarPackageDto(
    int Id,
    string VarName,
    string CreatorName,
    string PackageName,
    string Version,
    string FilePath,
    long FileSize,
    string? FileHash,
    int RepositoryId,
    string RepositoryName,
    string? LicenseType,
    string? Description,
    string? Credits,
    string? PreviewImagePath,
    DateTime? FileModifiedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    InstallationStatusDto? InstallationStatus
);

public record VarSearchQuery(
    string? CreatorName,
    string? PackageName,
    string? Version,
    string? LicenseType,
    int? RepositoryId,
    bool? IsInstalled,
    int? InstallationTargetId,
    string? SearchText,
    DateTime? CreatedAfter,
    DateTime? CreatedBefore,
    long? MinFileSize,
    long? MaxFileSize
)
{
    public int? Skip { get; init; }
    public int? Take { get; init; }
    public string? SortBy { get; init; }
    public bool SortDescending { get; init; }
}

public record VarPagedQuery(
    int Page,
    int PageSize,
    VarSearchQuery? Filters,
    string? SortBy,
    bool SortDescending
);

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public record InstallationStatusDto(
    int InstallationId,
    int InstallationTargetId,
    string InstallationTargetName,
    string SymlinkPath,
    bool IsEnabled,
    DateTime InstalledAt
);

public record VarPackageMetadata(
    string VarName,
    string CreatorName,
    string PackageName,
    string Version,
    long FileSize,
    string FileHash,
    string? LicenseType,
    string? Description,
    string? Credits,
    string? Instructions,
    string? PromotionalLink,
    string? ProgramVersion,
    IEnumerable<string> ContentList,
    Dictionary<string, DependencyInfo> Dependencies
);

public record DependencyInfo(
    string DependencyName,
    string? LicenseType,
    Dictionary<string, DependencyInfo>? NestedDependencies
);
```

---

## 3. Installation Service

### Interface Definition

```csharp
public interface IInstallationService
{
    // Commands
    Task<Result<int>> InstallVarPackageAsync(InstallVarPackageCommand command, CancellationToken ct);
    Task<Result> UninstallVarPackageAsync(UninstallVarPackageCommand command, CancellationToken ct);
    Task<Result> BatchInstallVarPackagesAsync(BatchInstallCommand command, CancellationToken ct);
    Task<Result> BatchUninstallVarPackagesAsync(BatchUninstallCommand command, CancellationToken ct);
    Task<Result> EnableInstallationAsync(int installationId, bool enabled, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByVarPackageAsync(
        int varPackageId,
        CancellationToken ct);
    Task<Result<IEnumerable<InstallationDto>>> GetInstallationsByTargetAsync(
        int targetId,
        CancellationToken ct);
    Task<Result<InstallationDto>> GetInstallationAsync(int installationId, CancellationToken ct);
    
    // Operations
    Task<Result> VerifyInstallationsAsync(int targetId, CancellationToken ct);
    Task<Result<int>> RepairBrokenSymlinksAsync(int targetId, CancellationToken ct);
}
```

### DTOs

```csharp
public record InstallVarPackageCommand(
    int VarPackageId,
    int InstallationTargetId,
    bool InstallDependencies = false,
    bool SkipIfAlreadyInstalled = true
);

public record UninstallVarPackageCommand(
    int VarPackageId,
    int InstallationTargetId,
    bool RemoveSymlink = true
);

public record BatchInstallCommand(
    IEnumerable<int> VarPackageIds,
    int InstallationTargetId,
    bool InstallDependencies = false
);

public record BatchUninstallCommand(
    IEnumerable<int> VarPackageIds,
    int InstallationTargetId
);

public record InstallationDto(
    int Id,
    int VarPackageId,
    string VarName,
    int InstallationTargetId,
    string InstallationTargetName,
    string SymlinkPath,
    bool IsEnabled,
    DateTime InstalledAt,
    string? InstalledBy
);

public record SymlinkVerificationResult(
    int InstallationId,
    bool Exists,
    bool IsValid,
    string? ErrorMessage
);
```

---

## 4. Installation Target Service

### Interface Definition

```csharp
public interface IInstallationTargetService
{
    // Commands
    Task<Result<int>> AddInstallationTargetAsync(AddInstallationTargetCommand command, CancellationToken ct);
    Task<Result> UpdateInstallationTargetAsync(UpdateInstallationTargetCommand command, CancellationToken ct);
    Task<Result> DeleteInstallationTargetAsync(int targetId, CancellationToken ct);
    Task<Result> SetActiveTargetAsync(int targetId, CancellationToken ct);
    
    // Queries
    Task<Result<IEnumerable<InstallationTargetDto>>> GetAllTargetsAsync(CancellationToken ct);
    Task<Result<InstallationTargetDto>> GetTargetByIdAsync(int id, CancellationToken ct);
    Task<Result<InstallationTargetDto?>> GetActiveTargetAsync(CancellationToken ct);
    Task<Result<IEnumerable<InstallationTargetDto>>> GetTargetsByProfileAsync(
        string profileName,
        CancellationToken ct);
}
```

### DTOs

```csharp
public record AddInstallationTargetCommand(
    string Name,
    string Path,
    string? ProfileName,
    string? Description
);

public record UpdateInstallationTargetCommand(
    int Id,
    string? Name,
    string? Path,
    string? ProfileName,
    string? Description
);

public record InstallationTargetDto(
    int Id,
    string Name,
    string Path,
    string? ProfileName,
    string? Description,
    bool IsActive,
    int InstallationCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
```

---

## 5. Dependency Service

### Interface Definition

```csharp
public interface IDependencyService
{
    // Operations
    Task<Result> ResolveDependenciesAsync(CancellationToken ct);
    Task<Result<IEnumerable<DependencyDto>>> GetDependenciesAsync(int varPackageId, CancellationToken ct);
    Task<Result<IEnumerable<VarPackageDto>>> GetReverseDependenciesAsync(
        int varPackageId,
        CancellationToken ct);
    Task<Result<DependencyValidationResult>> ValidateDependenciesAsync(
        int varPackageId,
        CancellationToken ct);
}
```

### DTOs

```csharp
public record DependencyDto(
    int Id,
    string DependencyName,
    string? VersionConstraint,
    bool IsOptional,
    bool IsResolved,
    int? ResolvedVarPackageId,
    string? ResolvedVarName
);

public record DependencyValidationResult(
    int VarPackageId,
    IEnumerable<MissingDependencyDto> MissingDependencies,
    IEnumerable<ResolvedDependencyDto> ResolvedDependencies,
    bool AllDependenciesResolved
);

public record MissingDependencyDto(
    string DependencyName,
    string? VersionConstraint,
    bool IsOptional
);

public record ResolvedDependencyDto(
    string DependencyName,
    int VarPackageId,
    string VarName,
    string RepositoryName
);
```

---

## 6. Search Service

### Interface Definition

```csharp
public interface ISearchService
{
    Task<Result<PagedResult<VarPackageDto>>> SearchAsync(
        VarSearchQuery query,
        CancellationToken ct);
    Task<Result<IEnumerable<string>>> GetCreatorsAsync(CancellationToken ct);
    Task<Result<IEnumerable<string>>> GetPackagesByCreatorAsync(
        string creatorName,
        CancellationToken ct);
    Task<Result<SearchStatisticsDto>> GetSearchStatisticsAsync(CancellationToken ct);
}
```

### DTOs

```csharp
public record SearchStatisticsDto(
    int TotalVarPackages,
    int TotalRepositories,
    int TotalInstallations,
    int UniqueCreators,
    long TotalFileSize,
    Dictionary<string, int> VarPackagesByCreator,
    Dictionary<string, int> VarPackagesByLicenseType
);
```

---

## 7. File System Service

### Interface Definition

```csharp
public interface IFileSystemService
{
    // Symbolic Links
    Task<Result> CreateSymbolicLinkAsync(string linkPath, string targetPath, bool isDirectory, CancellationToken ct);
    Task<Result> DeleteSymbolicLinkAsync(string linkPath, CancellationToken ct);
    Task<Result<bool>> SymbolicLinkExistsAsync(string linkPath, CancellationToken ct);
    Task<Result<string>> ResolveSymbolicLinkAsync(string linkPath, CancellationToken ct);
    
    // File Operations
    Task<Result<FileInfoDto>> GetFileInfoAsync(string filePath, CancellationToken ct);
    Task<Result<bool>> FileExistsAsync(string filePath, CancellationToken ct);
    Task<Result> DeleteFileAsync(string filePath, CancellationToken ct);
    Task<Result> MoveFileAsync(string sourcePath, string destinationPath, CancellationToken ct);
    
    // Directory Operations
    Task<Result<IEnumerable<string>>> GetFilesAsync(
        string directoryPath,
        string searchPattern,
        SearchOption searchOption,
        CancellationToken ct);
    Task<Result<bool>> DirectoryExistsAsync(string directoryPath, CancellationToken ct);
    Task<Result> CreateDirectoryAsync(string directoryPath, CancellationToken ct);
    
    // Validation
    Task<Result<PathValidationResult>> ValidatePathAsync(string path, CancellationToken ct);
}
```

### DTOs

```csharp
public record FileInfoDto(
    string FullPath,
    string Name,
    long Length,
    DateTime CreationTime,
    DateTime LastWriteTime,
    FileAttributes Attributes
);

public record PathValidationResult(
    bool IsValid,
    bool Exists,
    bool IsReadable,
    bool IsWritable,
    bool IsDirectory,
    string? ErrorMessage
);
```

---

## 8. Result Pattern

### Result<T> Implementation

```csharp
public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string? Error { get; private set; }
    public ErrorCode? ErrorCode { get; private set; }
    
    private Result(bool isSuccess, T? value, string? error, ErrorCode? errorCode)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
        ErrorCode = errorCode;
    }
    
    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string error, ErrorCode errorCode) => 
        new(false, default, error, errorCode);
    
    public static implicit operator Result<T>(T value) => Success(value);
}

public class Result
{
    public bool IsSuccess { get; private set; }
    public string? Error { get; private set; }
    public ErrorCode? ErrorCode { get; private set; }
    
    private Result(bool isSuccess, string? error, ErrorCode? errorCode)
    {
        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }
    
    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, ErrorCode errorCode) => 
        new(false, error, errorCode);
}

public enum ErrorCode
{
    // General
    Unknown,
    NotFound,
    ValidationError,
    Unauthorized,
    
    // Repository
    RepositoryNotFound,
    RepositoryPathInvalid,
    RepositoryAccessDenied,
    RepositoryAlreadyExists,
    
    // VAR Package
    VarPackageNotFound,
    VarPackageInvalid,
    VarPackageCorrupted,
    VarPackageAlreadyExists,
    
    // Installation
    InstallationNotFound,
    InstallationFailed,
    SymlinkCreationFailed,
    TargetDirectoryInvalid,
    
    // Dependency
    DependencyNotFound,
    DependencyUnresolved,
    CircularDependency,
    
    // File System
    FileNotFound,
    DirectoryNotFound,
    AccessDenied,
    PathInvalid,
    SymlinkInvalid
}
```

---

## 9. Error Handling

### Exception vs Result Pattern

- **Exceptions**: Only for unexpected system errors
- **Result Pattern**: For expected business logic failures

### Example Usage

```csharp
public async Task<Result<int>> InstallVarPackageAsync(
    InstallVarPackageCommand command,
    CancellationToken ct)
{
    // Validate
    var varPackage = await _repository.GetByIdAsync(command.VarPackageId, ct);
    if (varPackage == null)
        return Result<int>.Failure("VAR package not found", ErrorCode.VarPackageNotFound);
    
    // Business logic with Result pattern
    var validationResult = await ValidateInstallationAsync(varPackage, command, ct);
    if (!validationResult.IsSuccess)
        return Result<int>.Failure(validationResult.Error, validationResult.ErrorCode);
    
    // Success path
    var installation = await CreateInstallationAsync(varPackage, command, ct);
    return Result<int>.Success(installation.Id);
}
```

---

## 10. Event Publishing

### Domain Events

```csharp
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}

public record VarPackageInstalledEvent(
    int VarPackageId,
    int InstallationTargetId,
    string VarName
) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public record RepositoryScannedEvent(
    int RepositoryId,
    int FilesScanned,
    int FilesAdded
) : IDomainEvent
{
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

### Event Publisher

```csharp
public interface IEventPublisher
{
    Task PublishAsync<T>(T domainEvent, CancellationToken ct) where T : IDomainEvent;
}
```

---

## Next Steps

Continue to:
- [UI Design](./05-UI-Design.md) - User interface design
- [Migration Plan](./06-Migration-Plan.md) - Migration strategy

