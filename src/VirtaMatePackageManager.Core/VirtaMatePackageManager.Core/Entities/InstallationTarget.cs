namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents an installation target directory where symbolic links are created.
/// </summary>
public class InstallationTarget
{
    public int Id { get; set; }
    
    /// <summary>
    /// Human-readable name for the installation target.
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full path to the target directory (e.g., "C:\VaM\AddonPackages").
    /// </summary>
    public string Path { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional profile name for switching contexts.
    /// </summary>
    public string? ProfileName { get; set; }
    
    /// <summary>
    /// Optional description.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Whether this target is currently active.
    /// Only one target can be active at a time.
    /// </summary>
    public bool IsActive { get; set; }
    
    /// <summary>
    /// Timestamp when target was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public ICollection<Installation> Installations { get; set; } = new List<Installation>();
}

