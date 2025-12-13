using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.ValueObjects.Validation;

/// <summary>
/// Result of VAR file validation.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Parsed filename components if validation passed.
    /// </summary>
    public VarFileNameComponents? ParsedName { get; set; }
    
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success(VarFileNameComponents parsedName) =>
        new()
        {
            IsValid = true,
            ParsedName = parsedName
        };
    
    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static ValidationResult Failure(string error) =>
        new()
        {
            IsValid = false,
            Error = error
        };
}


