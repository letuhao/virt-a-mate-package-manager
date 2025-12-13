namespace VirtaMatePackageManager.Core.ValueObjects.Validation;

/// <summary>
/// Result of complete VAR file validation (filename, structure, and metadata consistency).
/// </summary>
public class CompleteValidationResult
{
    /// <summary>
    /// Whether the complete validation passed.
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Overall error message if validation failed.
    /// </summary>
    public string? OverallError { get; set; }
    
    /// <summary>
    /// List of warnings (non-critical issues).
    /// </summary>
    public List<string> Warnings { get; set; } = new();
    
    /// <summary>
    /// Result of filename validation.
    /// </summary>
    public ValidationResult? FilenameValidation { get; set; }
    
    /// <summary>
    /// Result of structure validation.
    /// </summary>
    public StructureValidationResult? StructureValidation { get; set; }
    
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static CompleteValidationResult Success(
        ValidationResult filenameValidation,
        StructureValidationResult structureValidation) =>
        new()
        {
            IsValid = true,
            FilenameValidation = filenameValidation,
            StructureValidation = structureValidation
        };
    
    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static CompleteValidationResult Failure(string error) =>
        new()
        {
            IsValid = false,
            OverallError = error
        };
}

