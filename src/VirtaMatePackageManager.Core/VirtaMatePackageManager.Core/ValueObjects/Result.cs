namespace VirtaMatePackageManager.Core.ValueObjects;

/// <summary>
/// Result pattern for error handling without exceptions.
/// </summary>
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

/// <summary>
/// Non-generic Result for operations that don't return a value.
/// </summary>
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

/// <summary>
/// Error codes for different failure scenarios.
/// </summary>
public enum ErrorCode
{
    // General
    Unknown = 0,
    NotFound = 1,
    ValidationError = 2,
    Unauthorized = 3,
    InvalidInput = 4,
    IOError = 5,
    
    // Repository
    RepositoryNotFound = 100,
    RepositoryPathInvalid = 101,
    RepositoryAccessDenied = 102,
    RepositoryAlreadyExists = 103,
    
    // VAR Package
    VarPackageNotFound = 200,
    VarPackageInvalid = 201,
    VarPackageCorrupted = 202,
    VarPackageAlreadyExists = 203,
    
    // Installation
    InstallationNotFound = 300,
    InstallationFailed = 301,
    SymlinkCreationFailed = 302,
    TargetDirectoryInvalid = 303,
    InstallationTargetNotFound = 304,
    AlreadyInstalled = 305,
    PathConflict = 306,
    DirectoryCreationFailed = 307,
    
    // Dependency
    DependencyNotFound = 400,
    DependencyUnresolved = 401,
    CircularDependency = 402,
    
    // File System
    FileNotFound = 500,
    DirectoryNotFound = 501,
    AccessDenied = 502,
    PathInvalid = 503,
    SymlinkInvalid = 504,
    
    // Scanning
    ScanFailed = 600
}

