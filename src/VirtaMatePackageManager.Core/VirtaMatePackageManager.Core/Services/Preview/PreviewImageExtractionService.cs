using System.IO.Compression;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Core.Services.Content;
using VirtaMatePackageManager.Core.ValueObjects.Content;

namespace VirtaMatePackageManager.Core.Services.Preview;

/// <summary>
/// Information about an extracted preview image.
/// </summary>
public class PreviewImageInfo
{
    /// <summary>
    /// Path to the content item within the VAR package.
    /// </summary>
    public string ContentItemPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of content.
    /// </summary>
    public ContentType ContentType { get; set; }
    
    /// <summary>
    /// Full path to the extracted preview image.
    /// </summary>
    public string PreviewPath { get; set; } = string.Empty;
    
    /// <summary>
    /// Relative path from output directory.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Filename of the extracted preview image.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// Size of the preview image in bytes.
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// Whether the preview image was successfully extracted.
    /// </summary>
    public bool Extracted { get; set; }
}

/// <summary>
/// Service for extracting preview images from VAR packages.
/// </summary>
public class PreviewImageExtractionService
{
    private readonly ContentAnalysisService _contentAnalysisService;
    private readonly IUnitOfWork _unitOfWork;

    public PreviewImageExtractionService(
        ContentAnalysisService? contentAnalysisService = null,
        IUnitOfWork? unitOfWork = null)
    {
        _contentAnalysisService = contentAnalysisService ?? new ContentAnalysisService();
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    }

    /// <summary>
    /// Extracts preview images (JPG files) associated with content items from VAR file.
    /// </summary>
    /// <param name="varFilePath">Full path to VAR file</param>
    /// <param name="varPackageId">Database ID of VAR package</param>
    /// <param name="outputDirectory">Base directory for extracted previews</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of extracted preview images</returns>
    /// <exception cref="InvalidOperationException">Thrown when extraction fails</exception>
    public async Task<List<PreviewImageInfo>> ExtractPreviewImagesAsync(
        string varFilePath,
        int varPackageId,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var previewImages = new List<PreviewImageInfo>();

        try
        {
            using var zipFile = ZipFile.OpenRead(varFilePath);
            
            // Get all content items that should have previews
            var analysisResult = _contentAnalysisService.AnalyzeVarContent(varFilePath);
            
            // Track counts by content type for filename generation
            var contentTypeCounts = new Dictionary<ContentType, int>();
            
            foreach (var contentItem in analysisResult.ContentItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (ShouldHavePreview(contentItem.ContentType))
                {
                    var previewPath = FindPreviewImagePath(zipFile, contentItem.Path);
                    
                    if (previewPath != null)
                    {
                        var previewEntry = zipFile.GetEntry(previewPath);
                        
                        if (previewEntry != null)
                        {
                            // Get count for this content type
                            if (!contentTypeCounts.ContainsKey(contentItem.ContentType))
                            {
                                contentTypeCounts[contentItem.ContentType] = 0;
                            }
                            
                            contentTypeCounts[contentItem.ContentType]++;
                            
                            var previewImage = await ExtractSinglePreviewImageAsync(
                                zipFile: zipFile,
                                previewEntry: previewEntry,
                                contentItem: contentItem,
                                varPackageId: varPackageId,
                                outputDirectory: outputDirectory,
                                count: contentTypeCounts[contentItem.ContentType],
                                cancellationToken: cancellationToken);
                            
                            if (previewImage != null)
                            {
                                previewImages.Add(previewImage);
                            }
                        }
                    }
                }
            }
            
            return previewImages;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract preview images: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Determines if a content type should have a preview image.
    /// </summary>
    /// <param name="contentType">Content type to check</param>
    /// <returns>True if the content type should have a preview, false otherwise</returns>
    public static bool ShouldHavePreview(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Scene => true,
            ContentType.Look => true,
            ContentType.Clothing => true,
            ContentType.Hairstyle => true,
            ContentType.Asset => true,
            ContentType.Morph => true,
            ContentType.Pose => true,
            ContentType.Skin => true,
            _ => false
        };
    }

    /// <summary>
    /// Finds the preview image path within a ZIP archive for a given content item path.
    /// </summary>
    /// <param name="zipFile">ZIP archive</param>
    /// <param name="contentPath">Path to the content item</param>
    /// <returns>Path to preview image in ZIP, or null if not found</returns>
    public static string? FindPreviewImagePath(ZipArchive zipFile, string contentPath)
    {
        // Preview image has same path as content item but with .jpg extension
        var lastDotIndex = contentPath.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            var pathWithoutExtension = contentPath.Substring(0, lastDotIndex);
            var previewPath = pathWithoutExtension + ".jpg";
            
            // Check if preview exists in ZIP
            if (zipFile.GetEntry(previewPath) != null)
            {
                return previewPath;
            }
        }
        
        // Alternative: preview might be in same directory with different name
        var directory = Path.GetDirectoryName(contentPath)?.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(contentPath);
        
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return null;
        }
        
        // Try common preview naming patterns
        var previewPatterns = new[]
        {
            $"{directory}/{fileName}.jpg",
            $"{directory}/preview.jpg",
            $"{directory}/{fileName}_preview.jpg"
        };
        
        foreach (var pattern in previewPatterns)
        {
            if (zipFile.GetEntry(pattern) != null)
            {
                return pattern;
            }
        }
        
        return null; // No preview found
    }

    /// <summary>
    /// Extracts a single preview image from the ZIP archive.
    /// </summary>
    /// <param name="zipFile">ZIP archive</param>
    /// <param name="previewEntry">ZIP entry for the preview image</param>
    /// <param name="contentItem">Content item information</param>
    /// <param name="varPackageId">VAR package ID</param>
    /// <param name="outputDirectory">Base output directory</param>
    /// <param name="count">Sequence number for this content type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>PreviewImageInfo if successful, null otherwise</returns>
    private async Task<PreviewImageInfo?> ExtractSinglePreviewImageAsync(
        ZipArchive zipFile,
        ZipArchiveEntry previewEntry,
        ContentItemInfo contentItem,
        int varPackageId,
        string outputDirectory,
        int count,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get VAR package name for directory structure
            var varPackage = await _unitOfWork.VarPackages.GetByIdAsync(varPackageId, cancellationToken);
            if (varPackage == null)
            {
                return null;
            }
            
            // Determine output path structure
            // Format: {outputDirectory}/{contentType}/{varPackageName}/{typePrefix}{count}_{originalName}.jpg
            var contentTypeDir = contentItem.ContentType.ToString().ToLowerInvariant();
            var varPackageName = Path.GetFileNameWithoutExtension(varPackage.VarName);
            
            var outputBaseDir = Path.Combine(outputDirectory, contentTypeDir, varPackageName);
            
            // Create directory if it doesn't exist
            Directory.CreateDirectory(outputBaseDir);
            
            // Generate unique filename
            var typePrefix = GetTypePrefix(contentItem.ContentType);
            var originalName = Path.GetFileNameWithoutExtension(previewEntry.Name).ToLowerInvariant();
            var outputFileName = $"{typePrefix}{count:000}_{originalName}.jpg";
            var outputPath = Path.Combine(outputBaseDir, outputFileName);
            
            // Extract if not already exists
            if (!File.Exists(outputPath))
            {
                using var inputStream = previewEntry.Open();
                using var outputStream = File.Create(outputPath);
                await inputStream.CopyToAsync(outputStream, cancellationToken);
            }
            
            var relativePath = Path.GetRelativePath(outputDirectory, outputPath);
            
            return new PreviewImageInfo
            {
                ContentItemPath = contentItem.Path,
                ContentType = contentItem.ContentType,
                PreviewPath = outputPath,
                RelativePath = relativePath,
                FileName = outputFileName,
                Size = previewEntry.Length,
                Extracted = true
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract preview image: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the type prefix for a content type for use in filename generation.
    /// </summary>
    /// <param name="contentType">Content type</param>
    /// <returns>Prefix string</returns>
    public static string GetTypePrefix(ContentType contentType)
    {
        return contentType switch
        {
            ContentType.Scene => "scene",
            ContentType.Look => "look",
            ContentType.Clothing => "clothing",
            ContentType.Hairstyle => "hairstyle",
            ContentType.Asset => "asset",
            ContentType.Morph => "morph",
            ContentType.Pose => "pose",
            ContentType.Skin => "skin",
            _ => "unknown"
        };
    }
}

