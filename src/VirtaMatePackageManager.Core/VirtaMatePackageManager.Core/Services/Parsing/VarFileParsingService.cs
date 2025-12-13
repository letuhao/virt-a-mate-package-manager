using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VirtaMatePackageManager.Core.ValueObjects.Metadata;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Services.Parsing;

/// <summary>
/// Service for parsing VAR file names and metadata.
/// </summary>
public class VarFileParsingService
{
    /// <summary>
    /// Parses a VAR filename to extract creator, package, and version components.
    /// Throws exceptions if parsing fails.
    /// </summary>
    /// <param name="fileName">VAR filename (with or without .var extension)</param>
    /// <returns>VarFileNameComponents with parsed components</returns>
    /// <exception cref="ArgumentException">Thrown when filename is null or empty</exception>
    /// <exception cref="FormatException">Thrown when filename format is invalid</exception>
    public VarFileNameComponents ParseVarFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));
        }
        
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var parts = fileNameWithoutExtension.Split('.');
        
        if (parts.Length != 3)
        {
            throw new FormatException(
                $"Invalid VAR filename format. Expected Creator.Package.Version, got: {fileNameWithoutExtension}");
        }
        
        var creator = parts[0];
        var package = parts[1];
        var versionString = parts[2];
        
        // Parse version
        bool isLatest;
        string version;
        
        if (versionString.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            isLatest = true;
            version = "latest";
        }
        else
        {
            if (!int.TryParse(versionString, out var versionNumber))
            {
                throw new FormatException(
                    $"Invalid version format: {versionString}. Must be numeric or 'latest'");
            }
            
            if (versionNumber < 1)
            {
                throw new ArgumentException($"Version must be >= 1, got: {versionNumber}");
            }
            
            isLatest = false;
            version = versionNumber.ToString();
        }
        
        return new VarFileNameComponents
        {
            Creator = creator,
            Package = package,
            Version = version,
            IsLatest = isLatest,
            FullVarName = Path.GetFileName(fileName)
        };
    }
    
    /// <summary>
    /// Extracts metadata from a VAR file's meta.json.
    /// </summary>
    /// <param name="varFilePath">Full path to VAR file</param>
    /// <returns>VarMetadata object containing all extracted metadata</returns>
    /// <exception cref="FileNotFoundException">VAR file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">Invalid ZIP or JSON structure</exception>
    public VarMetadata ExtractVarMetadata(string varFilePath)
    {
        if (!File.Exists(varFilePath))
        {
            throw new FileNotFoundException($"VAR file not found: {varFilePath}", varFilePath);
        }
        
        try
        {
            using var zipFile = ZipFile.OpenRead(varFilePath);
            
            var metaJsonEntry = zipFile.GetEntry("meta.json");
            
            if (metaJsonEntry == null)
            {
                throw new InvalidOperationException("VAR file missing meta.json");
            }
            
            string jsonText;
            DateTime metaDate;
            
            using (var stream = metaJsonEntry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                jsonText = reader.ReadToEnd();
            }
            
            metaDate = metaJsonEntry.LastWriteTime.DateTime;
            
            // Parse JSON
            var jsonDocument = JsonDocument.Parse(jsonText);
            var root = jsonDocument.RootElement;
            
            // Extract basic metadata
            var metadata = new VarMetadata
            {
                RawJson = jsonText,
                MetaDate = metaDate
            };
            
            if (root.TryGetProperty("creatorName", out var creatorElement))
            {
                metadata.CreatorName = creatorElement.GetString();
            }
            
            if (root.TryGetProperty("packageName", out var packageElement))
            {
                metadata.PackageName = packageElement.GetString();
            }
            
            if (root.TryGetProperty("licenseType", out var licenseElement))
            {
                metadata.LicenseType = licenseElement.GetString();
            }
            
            if (root.TryGetProperty("description", out var descElement))
            {
                metadata.Description = descElement.GetString();
            }
            
            if (root.TryGetProperty("credits", out var creditsElement))
            {
                metadata.Credits = creditsElement.GetString();
            }
            
            if (root.TryGetProperty("instructions", out var instructionsElement))
            {
                metadata.Instructions = instructionsElement.GetString();
            }
            
            if (root.TryGetProperty("promotionalLink", out var promoElement))
            {
                metadata.PromotionalLink = promoElement.GetString();
            }
            
            if (root.TryGetProperty("programVersion", out var programElement))
            {
                metadata.ProgramVersion = programElement.GetString();
            }
            
            // Extract content list
            if (root.TryGetProperty("contentList", out var contentElement))
            {
                if (contentElement.ValueKind == JsonValueKind.Array)
                {
                    metadata.ContentList = new List<string>();
                    foreach (var item in contentElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var contentItem = item.GetString();
                            if (!string.IsNullOrWhiteSpace(contentItem))
                            {
                                metadata.ContentList.Add(contentItem);
                            }
                        }
                    }
                }
            }
            
            // Extract dependencies flag
            if (root.TryGetProperty("dependencies", out var depsElement))
            {
                metadata.HasDependencies = depsElement.ValueKind == JsonValueKind.Object;
            }
            
            // Extract custom options
            if (root.TryGetProperty("customOptions", out var optionsElement))
            {
                metadata.CustomOptions = ParseCustomOptions(optionsElement);
            }
            
            return metadata;
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"Failed to read VAR file as ZIP: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse meta.json: {ex.Message}", ex);
        }
    }
    
    private Dictionary<string, object> ParseCustomOptions(JsonElement optionsElement)
    {
        var customOptions = new Dictionary<string, object>();
        
        if (optionsElement.ValueKind != JsonValueKind.Object)
        {
            return customOptions;
        }
        
        foreach (var option in optionsElement.EnumerateObject())
        {
            var key = option.Name;
            var value = option.Value;
            
            object? parsedValue = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => value.EnumerateArray().Select(e => e.GetString()).Where(s => s != null).ToList(),
                _ => value.GetRawText()
            };
            
            if (parsedValue != null)
            {
                customOptions[key] = parsedValue;
            }
        }
        
        return customOptions;
    }
}

