namespace VirtaMatePackageManager.Core.ValueObjects.Metadata;

/// <summary>
/// Extracted metadata from a VAR file's meta.json.
/// </summary>
public class VarMetadata
{
    /// <summary>
    /// Creator name from meta.json.
    /// </summary>
    public string? CreatorName { get; set; }
    
    /// <summary>
    /// Package name from meta.json.
    /// </summary>
    public string? PackageName { get; set; }
    
    /// <summary>
    /// License type (e.g., "Free", "Paid", "CC", etc.).
    /// </summary>
    public string? LicenseType { get; set; }
    
    /// <summary>
    /// Package description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Credits/attribution information.
    /// </summary>
    public string? Credits { get; set; }
    
    /// <summary>
    /// Usage instructions.
    /// </summary>
    public string? Instructions { get; set; }
    
    /// <summary>
    /// Promotional link (URL).
    /// </summary>
    public string? PromotionalLink { get; set; }
    
    /// <summary>
    /// Program version used to create this VAR.
    /// </summary>
    public string? ProgramVersion { get; set; }
    
    /// <summary>
    /// List of content items (file paths within the VAR).
    /// </summary>
    public List<string> ContentList { get; set; } = new();
    
    /// <summary>
    /// Whether the VAR has dependencies.
    /// </summary>
    public bool HasDependencies { get; set; }
    
    /// <summary>
    /// Custom options (key-value pairs).
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; set; } = new();
    
    /// <summary>
    /// Raw JSON text of meta.json.
    /// </summary>
    public string? RawJson { get; set; }
    
    /// <summary>
    /// Last write time of meta.json in the ZIP archive.
    /// </summary>
    public DateTime? MetaDate { get; set; }
}

