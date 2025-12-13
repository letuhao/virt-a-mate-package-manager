namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents a VAR (VaM Addon Resource) file package.
/// VAR name is globally unique - the game only accepts unique VAR filenames.
/// </summary>
public class VarPackage
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to the repository containing this VAR file.
    /// </summary>
    public int RepositoryId { get; set; }
    
    /// <summary>
    /// Full VAR name (e.g., "Creator.Package.1.var").
    /// This is globally unique - UNIQUE constraint in database.
    /// </summary>
    public string VarName { get; set; } = string.Empty;
    
    /// <summary>
    /// Creator name parsed from VAR filename.
    /// </summary>
    public string CreatorName { get; set; } = string.Empty;
    
    /// <summary>
    /// Package name parsed from VAR filename.
    /// </summary>
    public string PackageName { get; set; } = string.Empty;
    
    /// <summary>
    /// Version parsed from VAR filename (numeric or "latest").
    /// </summary>
    public string Version { get; set; } = string.Empty;
    
    /// <summary>
    /// Full absolute path to the VAR file.
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// SHA-256 hash of the file for duplicate detection.
    /// </summary>
    public string? FileHash { get; set; }
    
    /// <summary>
    /// Path relative to repository root.
    /// </summary>
    public string? RelativePath { get; set; }
    
    // Metadata from meta.json
    public string? LicenseType { get; set; }
    public string? Description { get; set; }
    public string? Credits { get; set; }
    public string? Instructions { get; set; }
    public string? PromotionalLink { get; set; }
    public string? ProgramVersion { get; set; }
    
    /// <summary>
    /// Path to extracted preview image.
    /// </summary>
    public string? PreviewImagePath { get; set; }
    
    // File timestamps
    public DateTime? FileCreatedAt { get; set; }
    public DateTime? FileModifiedAt { get; set; }
    
    // Database timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastScannedAt { get; set; }
    
    // Navigation properties
    public Repository Repository { get; set; } = null!;
    public ICollection<Dependency> Dependencies { get; set; } = new List<Dependency>();
    public ICollection<ContentItem> ContentItems { get; set; } = new List<ContentItem>();
    public ICollection<Installation> Installations { get; set; } = new List<Installation>();
}

