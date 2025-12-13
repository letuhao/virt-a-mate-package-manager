namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents a content item within a VAR package (scene, look, clothing, etc.).
/// </summary>
public class ContentItem
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to the VAR package containing this content.
    /// </summary>
    public int VarPackageId { get; set; }
    
    /// <summary>
    /// Type of content (Scene, Look, Clothing, Hairstyle, etc.).
    /// </summary>
    public ContentType ContentType { get; set; }
    
    /// <summary>
    /// Path to the content file within the VAR package.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether this content item is a preset.
    /// </summary>
    public bool IsPreset { get; set; }
    
    /// <summary>
    /// Optional: Path to preview image for this content item.
    /// </summary>
    public string? PreviewImagePath { get; set; }
    
    /// <summary>
    /// Optional: File size in bytes.
    /// </summary>
    public long? FileSize { get; set; }
    
    /// <summary>
    /// Timestamp when content item was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public VarPackage VarPackage { get; set; } = null!;
}

/// <summary>
/// Content type enumeration.
/// </summary>
public enum ContentType
{
    Unknown = 0,
    Scene = 1,
    Look = 2,
    Clothing = 3,
    Hairstyle = 4,
    Script = 5,
    ScriptList = 6,
    Asset = 7,
    Morph = 8,
    Pose = 9,
    Skin = 10
}

