namespace VirtaMatePackageManager.Core.Entities;

/// <summary>
/// Represents a dependency relationship between VAR packages.
/// </summary>
public class Dependency
{
    public int Id { get; set; }
    
    /// <summary>
    /// Foreign key to the VAR package that has this dependency.
    /// </summary>
    public int VarPackageId { get; set; }
    
    /// <summary>
    /// Dependency name (e.g., "Creator.Package.latest" or "Creator.Package.1").
    /// </summary>
    public string DependencyName { get; set; } = string.Empty;
    
    /// <summary>
    /// Foreign key to the resolved VAR package (if found).
    /// Null if dependency is not resolved.
    /// </summary>
    public int? ResolvedVarPackageId { get; set; }
    
    /// <summary>
    /// Version constraint (e.g., "latest", "1", ">=1").
    /// </summary>
    public string? VersionConstraint { get; set; }
    
    /// <summary>
    /// Whether this dependency is optional.
    /// </summary>
    public bool IsOptional { get; set; }
    
    /// <summary>
    /// Whether the dependency has been resolved.
    /// </summary>
    public bool IsResolved { get; set; }
    
    /// <summary>
    /// Timestamp when dependency was added.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public VarPackage VarPackage { get; set; } = null!;
    public VarPackage? ResolvedVarPackage { get; set; }
}

