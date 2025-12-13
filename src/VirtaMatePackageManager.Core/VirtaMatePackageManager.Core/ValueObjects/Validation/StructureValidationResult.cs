using System.Text.Json;

namespace VirtaMatePackageManager.Core.ValueObjects.Validation;

/// <summary>
/// Result of VAR file structure validation (ZIP and meta.json).
/// </summary>
public class StructureValidationResult
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
    /// List of missing files.
    /// </summary>
    public List<string> MissingFiles { get; set; } = new();
    
    /// <summary>
    /// List of missing required fields in meta.json.
    /// </summary>
    public List<string> MissingFields { get; set; } = new();
    
    /// <summary>
    /// Parsed metadata JSON if validation passed.
    /// </summary>
    public JsonDocument? Metadata { get; set; }
    
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static StructureValidationResult Success(JsonDocument metadata) =>
        new()
        {
            IsValid = true,
            Metadata = metadata
        };
    
    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static StructureValidationResult Failure(string error) =>
        new()
        {
            IsValid = false,
            Error = error
        };
    
    /// <summary>
    /// Creates a failed validation result with missing files.
    /// </summary>
    public static StructureValidationResult FailureWithMissingFiles(string error, List<string> missingFiles) =>
        new()
        {
            IsValid = false,
            Error = error,
            MissingFiles = missingFiles
        };
    
    /// <summary>
    /// Creates a failed validation result with missing fields.
    /// </summary>
    public static StructureValidationResult FailureWithMissingFields(string error, List<string> missingFields) =>
        new()
        {
            IsValid = false,
            Error = error,
            MissingFields = missingFields
        };
}


