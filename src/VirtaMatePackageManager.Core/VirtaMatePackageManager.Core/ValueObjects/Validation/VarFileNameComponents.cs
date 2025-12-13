namespace VirtaMatePackageManager.Core.ValueObjects.Validation;

/// <summary>
/// Components of a parsed VAR filename.
/// </summary>
public class VarFileNameComponents
{
    /// <summary>
    /// Creator name (e.g., "CreatorName").
    /// </summary>
    public string Creator { get; set; } = string.Empty;
    
    /// <summary>
    /// Package name (e.g., "PackageName").
    /// </summary>
    public string Package { get; set; } = string.Empty;
    
    /// <summary>
    /// Version string (e.g., "1", "2", or "latest").
    /// </summary>
    public string Version { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether version is "latest".
    /// </summary>
    public bool IsLatest { get; set; }
    
    /// <summary>
    /// Full VAR name (e.g., "Creator.Package.1.var").
    /// </summary>
    public string FullVarName { get; set; } = string.Empty;
}


