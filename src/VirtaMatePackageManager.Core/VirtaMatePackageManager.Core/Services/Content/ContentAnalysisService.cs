using System.IO.Compression;
using System.Text.RegularExpressions;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.ValueObjects.Content;

namespace VirtaMatePackageManager.Core.Services.Content;

/// <summary>
/// Service for analyzing VAR file content and categorizing entries by type.
/// </summary>
public class ContentAnalysisService
{
    private static readonly Regex ScenePattern = new(@"saves/scene/.*?\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LookPattern1 = new(@"saves/person/appearance/.*?\.(json|vac)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LookPattern2 = new(@"custom/atom/person/(general|appearance)/.*?\.(json|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClothingPattern1 = new(@"custom/clothing/.*?\.(vam|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClothingPattern2 = new(@"custom/atom/person/clothing/.*?\.(vam|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HairstylePattern1 = new(@"custom/hair/.*?\.(vam|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HairstylePattern2 = new(@"custom/atom/person/hair/.*?\.(vam|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScriptPattern = new(@"custom/(scripts|atom/person/scripts)/.*?\.cs$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ScriptListPattern = new(@"custom/(scripts|atom/person/scripts)/.*?\.cslist$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AssetPattern = new(@"custom/assets/.*?\.assetbundle$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MorphPattern = new(@"custom/atom/person/morphs/.*?\.(vmi|vap)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PosePattern1 = new(@"saves/person/pose/.*?\.(json|vac)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PosePattern2 = new(@"custom/atom/person/pose/.*?\.vap$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SkinPattern = new(@"custom/atom/person/skin/.*?\.vap$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    
    /// <summary>
    /// Analyzes VAR file content and categorizes all entries by type.
    /// </summary>
    /// <param name="varFilePath">Full path to VAR file</param>
    /// <returns>ContentAnalysisResult containing counts and detailed items</returns>
    /// <exception cref="InvalidOperationException">Thrown when analysis fails</exception>
    public ContentAnalysisResult AnalyzeVarContent(string varFilePath)
    {
        var contentCounts = new ContentCounts();
        var contentItems = new List<ContentItemInfo>();
        
        try
        {
            using var zipFile = ZipFile.OpenRead(varFilePath);
            
            foreach (var entry in zipFile.Entries)
            {
                // Skip directories
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    continue;
                }
                
                var contentType = DetermineContentType(entry.FullName);
                
                if (contentType != ContentType.Unknown)
                {
                    var isPreset = DetermineIfPreset(entry.FullName, contentType);
                    
                    var contentItem = new ContentItemInfo
                    {
                        ContentType = contentType,
                        Path = entry.FullName,
                        IsPreset = isPreset,
                        FileSize = entry.Length,
                        LastModified = entry.LastWriteTime.DateTime
                    };
                    
                    contentItems.Add(contentItem);
                    IncrementCount(contentCounts, contentType);
                }
                else
                {
                    contentCounts.Unknown++;
                }
            }
            
            return new ContentAnalysisResult
            {
                ContentCounts = contentCounts,
                ContentItems = contentItems
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze VAR content: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Determines the content type from an entry path.
    /// </summary>
    /// <param name="entryPath">Path to the entry within the VAR file</param>
    /// <returns>ContentType enum value</returns>
    public ContentType DetermineContentType(string entryPath)
    {
        var entryPathLower = entryPath.ToLowerInvariant();
        
        // Scenes
        if (ScenePattern.IsMatch(entryPathLower))
        {
            return ContentType.Scene;
        }
        
        // Looks - two patterns
        if (LookPattern1.IsMatch(entryPathLower) || LookPattern2.IsMatch(entryPathLower))
        {
            return ContentType.Look;
        }
        
        // Clothing - two patterns
        if (ClothingPattern1.IsMatch(entryPathLower) || ClothingPattern2.IsMatch(entryPathLower))
        {
            return ContentType.Clothing;
        }
        
        // Hairstyle - two patterns
        if (HairstylePattern1.IsMatch(entryPathLower) || HairstylePattern2.IsMatch(entryPathLower))
        {
            return ContentType.Hairstyle;
        }
        
        // Scripts
        if (ScriptPattern.IsMatch(entryPathLower))
        {
            return ContentType.Script;
        }
        
        if (ScriptListPattern.IsMatch(entryPathLower))
        {
            return ContentType.ScriptList;
        }
        
        // Assets
        if (AssetPattern.IsMatch(entryPathLower))
        {
            return ContentType.Asset;
        }
        
        // Morphs
        if (MorphPattern.IsMatch(entryPathLower))
        {
            return ContentType.Morph;
        }
        
        // Poses - two patterns
        if (PosePattern1.IsMatch(entryPathLower) || PosePattern2.IsMatch(entryPathLower))
        {
            return ContentType.Pose;
        }
        
        // Skin
        if (SkinPattern.IsMatch(entryPathLower))
        {
            return ContentType.Skin;
        }
        
        return ContentType.Unknown;
    }
    
    /// <summary>
    /// Determines if a content item is a preset based on its path and type.
    /// </summary>
    /// <param name="entryPath">Path to the entry within the VAR file</param>
    /// <param name="contentType">Type of content</param>
    /// <returns>True if the content item is a preset, false otherwise</returns>
    public bool DetermineIfPreset(string entryPath, ContentType contentType)
    {
        var entryPathLower = entryPath.ToLowerInvariant();
        
        return contentType switch
        {
            ContentType.Look => entryPathLower.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                               entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Clothing => entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Hairstyle => entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Morph => entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Pose => entryPathLower.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                               entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Skin => entryPathLower.EndsWith(".vap", StringComparison.OrdinalIgnoreCase),
            
            ContentType.Scene => true, // Scenes are always presets
            
            _ => false
        };
    }
    
    /// <summary>
    /// Increments the count for a specific content type.
    /// </summary>
    /// <param name="counts">ContentCounts object to update</param>
    /// <param name="contentType">Content type to increment</param>
    private static void IncrementCount(ContentCounts counts, ContentType contentType)
    {
        switch (contentType)
        {
            case ContentType.Scene:
                counts.Scenes++;
                break;
            case ContentType.Look:
                counts.Looks++;
                break;
            case ContentType.Clothing:
                counts.Clothing++;
                break;
            case ContentType.Hairstyle:
                counts.Hairstyles++;
                break;
            case ContentType.Script:
                counts.Scripts++;
                break;
            case ContentType.ScriptList:
                counts.ScriptLists++;
                break;
            case ContentType.Asset:
                counts.Assets++;
                break;
            case ContentType.Morph:
                counts.Morphs++;
                break;
            case ContentType.Pose:
                counts.Poses++;
                break;
            case ContentType.Skin:
                counts.Skins++;
                break;
            case ContentType.Unknown:
                counts.Unknown++;
                break;
        }
    }
}

