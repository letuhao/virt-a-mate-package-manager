using System.Security.Cryptography;

namespace VirtaMatePackageManager.Core.Services.FileSystem;

/// <summary>
/// Service for computing file hashes (SHA-256).
/// </summary>
public class FileHashService
{
    /// <summary>
    /// Computes SHA-256 hash of a file synchronously.
    /// </summary>
    /// <param name="filePath">Full path to the file</param>
    /// <returns>SHA-256 hash as hexadecimal string</returns>
    /// <exception cref="ArgumentException">File path is null or empty</exception>
    /// <exception cref="FileNotFoundException">File doesn't exist</exception>
    /// <exception cref="IOException">I/O error while reading file</exception>
    public string ComputeFileHash(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }
        
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
        }
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }
        
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        
        var hashBytes = sha256.ComputeHash(fileStream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    
    /// <summary>
    /// Computes SHA-256 hash of a file asynchronously.
    /// </summary>
    /// <param name="filePath">Full path to the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>SHA-256 hash as hexadecimal string</returns>
    /// <exception cref="ArgumentException">File path is null or empty</exception>
    /// <exception cref="FileNotFoundException">File doesn't exist</exception>
    /// <exception cref="IOException">I/O error while reading file</exception>
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }
        
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be empty", nameof(filePath));
        }
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }
        
        using var sha256 = SHA256.Create();
        await using var fileStream = File.OpenRead(filePath);
        
        var hashBytes = await sha256.ComputeHashAsync(fileStream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    
    /// <summary>
    /// Compares two files for identical content (size and hash).
    /// </summary>
    /// <param name="filePath1">First file path</param>
    /// <param name="filePath2">Second file path</param>
    /// <returns>True if files are identical, false otherwise</returns>
    public bool FilesAreIdentical(string filePath1, string filePath2)
    {
        // Quick check: compare file sizes first
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);
        
        if (!fileInfo1.Exists || !fileInfo2.Exists)
        {
            return false;
        }
        
        if (fileInfo1.Length != fileInfo2.Length)
        {
            return false;
        }
        
        // Compare hashes
        var hash1 = ComputeFileHash(filePath1);
        var hash2 = ComputeFileHash(filePath2);
        
        return hash1 == hash2;
    }
    
    /// <summary>
    /// Compares two files for identical content asynchronously (size and hash).
    /// </summary>
    /// <param name="filePath1">First file path</param>
    /// <param name="filePath2">Second file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if files are identical, false otherwise</returns>
    public async Task<bool> FilesAreIdenticalAsync(
        string filePath1,
        string filePath2,
        CancellationToken cancellationToken = default)
    {
        // Quick check: compare file sizes first
        var fileInfo1 = new FileInfo(filePath1);
        var fileInfo2 = new FileInfo(filePath2);
        
        if (!fileInfo1.Exists || !fileInfo2.Exists)
        {
            return false;
        }
        
        if (fileInfo1.Length != fileInfo2.Length)
        {
            return false;
        }
        
        // Compare hashes in parallel
        var hash1Task = ComputeFileHashAsync(filePath1, cancellationToken);
        var hash2Task = ComputeFileHashAsync(filePath2, cancellationToken);
        
        var hash1 = await hash1Task;
        var hash2 = await hash2Task;
        
        return hash1 == hash2;
    }
}

