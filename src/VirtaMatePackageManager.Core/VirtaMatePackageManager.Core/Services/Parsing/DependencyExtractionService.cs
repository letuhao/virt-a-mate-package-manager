using System.Text.Json;
using System.Text.RegularExpressions;
using VirtaMatePackageManager.Core.ValueObjects.Metadata;

namespace VirtaMatePackageManager.Core.Services.Parsing;

/// <summary>
/// Service for extracting and processing dependencies from VAR metadata.
/// </summary>
public class DependencyExtractionService
{
    private static readonly Regex VersionPattern = new(@"^[0-9]+$", RegexOptions.Compiled);
    
    /// <summary>
    /// Extracts dependencies from meta.json, including nested dependencies.
    /// </summary>
    /// <param name="metaJson">JSON element containing dependencies</param>
    /// <returns>List of DependencyInfo objects, flattened from nested structure</returns>
    public List<DependencyInfo> ExtractDependencies(JsonElement metaJson)
    {
        var dependencies = new List<DependencyInfo>();
        
        if (metaJson.ValueKind != JsonValueKind.Object)
        {
            return dependencies;
        }
        
        if (!metaJson.TryGetProperty("dependencies", out var depsElement))
        {
            return dependencies;
        }
        
        if (depsElement.ValueKind != JsonValueKind.Object)
        {
            return dependencies;
        }
        
        // Recursively extract dependencies
        ExtractDependenciesRecursive(depsElement, dependencies, parentPath: "");
        
        // Remove duplicates based on Name
        return dependencies
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
    
    /// <summary>
    /// Recursively extracts dependencies from a JSON element.
    /// </summary>
    /// <param name="depsElement">JSON element containing dependencies</param>
    /// <param name="dependencies">List to add dependencies to</param>
    /// <param name="parentPath">Full path of the parent dependency</param>
    private void ExtractDependenciesRecursive(
        JsonElement depsElement,
        List<DependencyInfo> dependencies,
        string parentPath)
    {
        foreach (var dependencyProperty in depsElement.EnumerateObject())
        {
            var dependencyName = dependencyProperty.Name;
            var dependencyValue = dependencyProperty.Value;
            
            // Remove path prefix if present (e.g., "creator/package.version")
            if (dependencyName.Contains('/'))
            {
                var lastSlashIndex = dependencyName.LastIndexOf('/');
                if (lastSlashIndex >= 0 && lastSlashIndex < dependencyName.Length - 1)
                {
                    dependencyName = dependencyName.Substring(lastSlashIndex + 1);
                }
            }
            
            // Validate dependency name format
            if (!IsValidDependencyName(dependencyName))
            {
                continue; // Skip invalid dependency names
            }
            
            var dependencyInfo = new DependencyInfo
            {
                Name = dependencyName,
                FullPath = !string.IsNullOrEmpty(parentPath) 
                    ? $"{parentPath} > {dependencyName}" 
                    : dependencyName,
                Depth = CountDepth(parentPath)
            };
            
            // Extract license type and other properties
            if (dependencyValue.ValueKind == JsonValueKind.Object)
            {
                if (dependencyValue.TryGetProperty("licenseType", out var licenseElement))
                {
                    dependencyInfo.LicenseType = licenseElement.GetString();
                }
                
                // Check if missing
                if (dependencyValue.TryGetProperty("missing", out var missingElement))
                {
                    dependencyInfo.IsMissing = missingElement.GetBoolean();
                }
                
                // Check if optional
                if (dependencyValue.TryGetProperty("optional", out var optionalElement))
                {
                    dependencyInfo.IsOptional = optionalElement.GetBoolean();
                }
                // If not explicitly marked as optional, but marked as missing, consider it optional
                else if (dependencyInfo.IsMissing)
                {
                    dependencyInfo.IsOptional = true;
                }
                
                // Extract nested dependencies
                if (dependencyValue.TryGetProperty("dependencies", out var nestedDeps))
                {
                    if (nestedDeps.ValueKind == JsonValueKind.Object)
                    {
                        ExtractDependenciesRecursive(
                            nestedDeps,
                            dependencies,
                            dependencyInfo.FullPath
                        );
                    }
                }
            }
            
            dependencies.Add(dependencyInfo);
        }
    }
    
    /// <summary>
    /// Validates that a dependency name follows the required format.
    /// Format: Creator.Package.Version or Creator.Package.latest
    /// </summary>
    /// <param name="name">Dependency name to validate</param>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValidDependencyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        
        // Format: Creator.Package.Version or Creator.Package.latest
        var parts = name.Split('.');
        
        if (parts.Length < 2 || parts.Length > 3)
        {
            return false;
        }
        
        // Last part should be version (numeric) or "latest"
        var lastPart = parts[^1];
        
        if (lastPart.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        return VersionPattern.IsMatch(lastPart);
    }
    
    /// <summary>
    /// Counts the depth level from a dependency path.
    /// Depth = number of " > " separators in the path.
    /// </summary>
    /// <param name="path">Full dependency path</param>
    /// <returns>Depth level (0 = root, 1 = first level nested, etc.)</returns>
    private int CountDepth(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }
        
        // Count " > " separators
        return path.Split(new[] { " > " }, StringSplitOptions.None).Length - 1;
    }
    
    /// <summary>
    /// Flattens nested dependencies into a simple list with depth information.
    /// This is a utility function that can be used when you have a hierarchical structure
    /// and want to flatten it, handling circular dependencies.
    /// </summary>
    /// <param name="dependencies">List of DependencyInfo with potential nesting</param>
    /// <returns>Flattened list of dependencies with depth information</returns>
    public List<DependencyInfo> FlattenDependencies(List<DependencyInfo> dependencies)
    {
        var flattened = new List<DependencyInfo>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var dependency in dependencies)
        {
            ProcessDependency(dependency, flattened, visited, depth: 0);
        }
        
        // Remove duplicates by Name
        return flattened
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }
    
    private void ProcessDependency(
        DependencyInfo dependency,
        List<DependencyInfo> flattened,
        HashSet<string> visited,
        int depth)
    {
        if (visited.Contains(dependency.Name))
        {
            return; // Avoid infinite loops in circular dependencies
        }
        
        visited.Add(dependency.Name);
        
        flattened.Add(new DependencyInfo
        {
            Name = dependency.Name,
            FullPath = dependency.FullPath,
            Depth = depth,
            LicenseType = dependency.LicenseType,
            IsMissing = dependency.IsMissing
        });
        
        // Process nested dependencies if any
        if (dependency.NestedDependencies != null)
        {
            foreach (var nested in dependency.NestedDependencies)
            {
                ProcessDependency(nested, flattened, visited, depth + 1);
            }
        }
        
        visited.Remove(dependency.Name); // Backtrack for other paths
    }
}

