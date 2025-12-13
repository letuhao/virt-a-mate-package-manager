using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Services.Validation;

/// <summary>
/// Service for validating VAR file names and structures.
/// </summary>
public class VarFileValidationService
{
    private static readonly Regex CreatorNamePattern = new(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex PackageNamePattern = new(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"^[0-9]+$", RegexOptions.Compiled);
    
    private static readonly string[] RequiredMetaFields = { "creatorName", "packageName", "contentList" };
    
    /// <summary>
    /// Validates that a VAR filename follows the required naming convention.
    /// Naming Convention: Creator.Package.Version.var
    /// - Creator: 1-60 characters, alphanumeric and underscore
    /// - Package: 1-80 characters, alphanumeric and underscore
    /// - Version: Numeric (1, 2, 3...) or "latest"
    /// </summary>
    /// <param name="filePath">Full path to VAR file</param>
    /// <returns>ValidationResult with parsed components if valid</returns>
    public ValidationResult ValidateVarFileName(string filePath)
    {
        // Check if file path is empty
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ValidationResult.Failure("File path is empty");
        }
        
        // Get filename and extension
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        
        // Check extension
        if (extension != ".var")
        {
            return ValidationResult.Failure("File extension must be .var");
        }
        
        // Split filename by dots
        var parts = fileNameWithoutExtension.Split('.');
        
        // Check if we have exactly 3 parts
        if (parts.Length != 3)
        {
            return ValidationResult.Failure("Filename must have 3 parts separated by dots (Creator.Package.Version)");
        }
        
        var creator = parts[0];
        var package = parts[1];
        var version = parts[2];
        
        // Validate creator name (1-60 chars, alphanumeric + underscore)
        if (creator.Length < 1 || creator.Length > 60)
        {
            return ValidationResult.Failure("Creator name must be 1-60 characters");
        }
        
        if (!CreatorNamePattern.IsMatch(creator))
        {
            return ValidationResult.Failure("Creator name contains invalid characters. Only alphanumeric and underscore allowed.");
        }
        
        // Validate package name (1-80 chars, alphanumeric + underscore)
        if (package.Length < 1 || package.Length > 80)
        {
            return ValidationResult.Failure("Package name must be 1-80 characters");
        }
        
        if (!PackageNamePattern.IsMatch(package))
        {
            return ValidationResult.Failure("Package name contains invalid characters. Only alphanumeric and underscore allowed.");
        }
        
        // Validate version (numeric or "latest")
        if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            var parsedName = new VarFileNameComponents
            {
                Creator = creator,
                Package = package,
                Version = "latest",
                IsLatest = true,
                FullVarName = Path.GetFileName(filePath)
            };
            
            return ValidationResult.Success(parsedName);
        }
        
        if (!VersionPattern.IsMatch(version))
        {
            return ValidationResult.Failure("Version must be numeric or 'latest'");
        }
        
        // Parse version number
        if (!int.TryParse(version, out var versionNumber) || versionNumber < 1)
        {
            return ValidationResult.Failure("Version must be a positive integer (>= 1)");
        }
        
        var result = new VarFileNameComponents
        {
            Creator = creator,
            Package = package,
            Version = versionNumber.ToString(),
            IsLatest = false,
            FullVarName = Path.GetFileName(filePath)
        };
        
        return ValidationResult.Success(result);
    }
    
    /// <summary>
    /// Validates that a VAR file is a valid ZIP archive and contains required files.
    /// </summary>
    /// <param name="filePath">Full path to VAR file</param>
    /// <returns>StructureValidationResult with validation status and parsed metadata if valid</returns>
    public StructureValidationResult ValidateVarFileStructure(string filePath)
    {
        // Check if file exists
        if (!File.Exists(filePath))
        {
            return StructureValidationResult.Failure("File does not exist");
        }
        
        try
        {
            // Attempt to open as ZIP file
            using var zipFile = ZipFile.OpenRead(filePath);
            
            // Check if meta.json exists
            var metaJsonEntry = zipFile.GetEntry("meta.json");
            
            if (metaJsonEntry == null)
            {
                return StructureValidationResult.FailureWithMissingFiles(
                    "VAR file missing meta.json",
                    new List<string> { "meta.json" }
                );
            }
            
            // Validate meta.json is readable
            try
            {
                using var stream = metaJsonEntry.Open();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var jsonContent = reader.ReadToEnd();
                
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    return StructureValidationResult.Failure("meta.json is empty");
                }
                
                // Try to parse as JSON
                var jsonDocument = JsonDocument.Parse(jsonContent);
                
                if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return StructureValidationResult.Failure("meta.json is not a valid JSON object");
                }
                
                // Check for required fields in meta.json
                var missingFields = new List<string>();
                
                foreach (var field in RequiredMetaFields)
                {
                    if (!jsonDocument.RootElement.TryGetProperty(field, out _))
                    {
                        missingFields.Add(field);
                    }
                }
                
                if (missingFields.Count > 0)
                {
                    return StructureValidationResult.FailureWithMissingFields(
                        "meta.json missing required fields",
                        missingFields
                    );
                }
                
                // Return the JsonDocument - caller is responsible for disposing
                // We need to clone it since we're disposing the stream
                var jsonBytes = Encoding.UTF8.GetBytes(jsonContent);
                var clonedDocument = JsonDocument.Parse(jsonBytes);
                
                return StructureValidationResult.Success(clonedDocument);
            }
            catch (JsonException ex)
            {
                return StructureValidationResult.Failure($"meta.json is not valid JSON: {ex.Message}");
            }
        }
        catch (InvalidDataException ex)
        {
            return StructureValidationResult.Failure($"File is not a valid ZIP archive: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return StructureValidationResult.Failure($"Access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            return StructureValidationResult.Failure($"I/O error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Performs complete validation including filename, structure, and content integrity.
    /// </summary>
    /// <param name="filePath">Full path to VAR file</param>
    /// <returns>CompleteValidationResult with all validation results</returns>
    public CompleteValidationResult ValidateVarFileComplete(string filePath)
    {
        var result = new CompleteValidationResult();
        
        // Step 1: Validate filename
        var filenameValidation = ValidateVarFileName(filePath);
        result.FilenameValidation = filenameValidation;
        
        if (!filenameValidation.IsValid)
        {
            result.IsValid = false;
            result.OverallError = filenameValidation.Error;
            return result;
        }
        
        // Step 2: Validate file structure
        var structureValidation = ValidateVarFileStructure(filePath);
        result.StructureValidation = structureValidation;
        
        if (!structureValidation.IsValid)
        {
            result.IsValid = false;
            result.OverallError = structureValidation.Error;
            return result;
        }
        
        // Step 3: Validate filename matches metadata
        if (structureValidation.Metadata == null || filenameValidation.ParsedName == null)
        {
            result.IsValid = false;
            result.OverallError = "Validation data is missing";
            return result;
        }
        
        var metadata = structureValidation.Metadata.RootElement;
        var parsedName = filenameValidation.ParsedName;
        
        // Check creator name match
        if (metadata.TryGetProperty("creatorName", out var creatorNameElement))
        {
            var metaCreatorName = creatorNameElement.GetString();
            if (metaCreatorName != parsedName.Creator)
            {
                result.IsValid = false;
                result.OverallError = "Creator name in filename doesn't match meta.json";
                result.Warnings.Add("Creator name mismatch");
                
                // Dispose the JsonDocument
                structureValidation.Metadata?.Dispose();
                return result;
            }
        }
        
        // Check package name match
        if (metadata.TryGetProperty("packageName", out var packageNameElement))
        {
            var metaPackageName = packageNameElement.GetString();
            if (metaPackageName != parsedName.Package)
            {
                result.IsValid = false;
                result.OverallError = "Package name in filename doesn't match meta.json";
                result.Warnings.Add("Package name mismatch");
                
                // Dispose the JsonDocument
                structureValidation.Metadata?.Dispose();
                return result;
            }
        }
        
        // Step 4: Check file integrity (optional hash check)
        try
        {
            var fileInfo = new FileInfo(filePath);
            
            if (fileInfo.Length == 0)
            {
                result.IsValid = false;
                result.OverallError = "VAR file is empty";
                
                // Dispose the JsonDocument
                structureValidation.Metadata?.Dispose();
                return result;
            }
            
            if (fileInfo.Length < 100)
            {
                result.Warnings.Add("VAR file is suspiciously small");
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Could not check file size: {ex.Message}");
        }
        
        result.IsValid = true;
        return result;
    }
}

