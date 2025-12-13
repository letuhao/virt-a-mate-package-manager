namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents an installation of a VAR package to an installation target.
/// Installation is done via symbolic link.
/// </summary>
public class Installation
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to the VAR package being installed.
    /// </summary>
    public int VarPackageId { get; set; }
    
    /// <summary>
    /// Foreign key to the installation target.
    /// </summary>
    public int InstallationTargetId { get; set; }
    
    /// <summary>
    /// Path to the symbolic link (e.g., "C:\VaM\AddonPackages\Creator.Package.1.var").
    /// </summary>
    public string SymlinkPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the installation is enabled (symlink active).
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Timestamp when VAR was installed.
    /// </summary>
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Optional: User who installed the VAR.
    /// </summary>
    public string? InstalledBy { get; set; }
    
    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public VarPackage VarPackage { get; set; } = null!;
    public InstallationTarget InstallationTarget { get; set; } = null!;
}

