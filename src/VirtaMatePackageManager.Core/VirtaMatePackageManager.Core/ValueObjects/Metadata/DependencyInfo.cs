namespace VirtaMatePackageManager.Core.ValueObjects.Metadata;

/// <summary>
/// Information about a VAR package dependency extracted from meta.json.
/// </summary>
public class DependencyInfo
{
    /// <summary>
    /// Dependency name (e.g., "Creator.Package.1" or "Creator.Package.latest").
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full dependency path showing the hierarchy (e.g., "Package1 > Package2 > Package3").
    /// </summary>
    public string FullPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Depth level in the dependency tree (0 = direct dependency, 1 = nested, etc.).
    /// </summary>
    public int Depth { get; set; }
    
    /// <summary>
    /// License type of the dependency (if specified).
    /// </summary>
    public string? LicenseType { get; set; }
    
    /// <summary>
    /// Whether this dependency is marked as missing in meta.json.
    /// </summary>
    public bool IsMissing { get; set; }
    
    /// <summary>
    /// Whether this dependency is optional (if specified in meta.json).
    /// </summary>
    public bool IsOptional { get; set; }
    
    /// <summary>
    /// Nested dependencies (optional, for hierarchical structures).
    /// </summary>
    public List<DependencyInfo>? NestedDependencies { get; set; }
}

