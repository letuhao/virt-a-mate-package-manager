namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents a VAR file repository (storage location).
/// </summary>
public class Repository
{
    public int Id { get; set; }
    
    /// <summary>
    /// Human-readable name for the repository.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full path to the repository directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional description of the repository.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Priority for search order (higher = searched first).
    /// Default: 0
    /// </summary>
    public int Priority { get; set; }
    
    /// <summary>
    /// Whether the repository is enabled/active.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Timestamp when repository was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<VarPackage> VarPackages { get; set; } = new List<VarPackage>();
}

