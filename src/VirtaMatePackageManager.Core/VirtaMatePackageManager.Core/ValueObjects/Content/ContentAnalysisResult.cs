using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Core.ValueObjects.Content;

/// <summary>
/// Result of analyzing VAR file content.
/// </summary>
public class ContentAnalysisResult
{
    /// <summary>
    /// Content counts by type.
    /// </summary>
    public ContentCounts ContentCounts { get; set; } = new();
    
    /// <summary>
    /// List of all content items found.
    /// </summary>
    public List<ContentItemInfo> ContentItems { get; set; } = new();
    
    /// <summary>
    /// Total number of content items found.
    /// </summary>
    public int TotalItems => ContentItems.Count;
}

/// <summary>
/// Counts of content items by type.
/// </summary>
public class ContentCounts
{
    public int Scenes { get; set; }
    public int Looks { get; set; }
    public int Clothing { get; set; }
    public int Hairstyles { get; set; }
    public int Scripts { get; set; }
    public int ScriptLists { get; set; }
    public int Assets { get; set; }
    public int Morphs { get; set; }
    public int Poses { get; set; }
    public int Skins { get; set; }
    public int Unknown { get; set; }
}

/// <summary>
/// Information about a content item within a VAR package.
/// </summary>
public class ContentItemInfo
{
    /// <summary>
    /// Type of content.
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
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// Last modified time from ZIP entry.
    /// </summary>
    public DateTime? LastModified { get; set; }
}

