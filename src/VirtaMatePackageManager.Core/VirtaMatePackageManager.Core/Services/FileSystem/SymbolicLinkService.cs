using System.Runtime.InteropServices;
using VirtaMatePackageManager.Core.ValueObjects;

namespace VirtaMatePackageManager.Core.Services.FileSystem;

/// <summary>
/// Service for creating and managing Windows symbolic links.
/// </summary>
public class SymbolicLinkService
{
    // Windows API constants
    private const int SYMBOLIC_LINK_FLAG_FILE = 0x0;
    private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;
    private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateSymbolicLinkW(
        string lpSymlinkFileName,
        string lpTargetFileName,
        int dwFlags);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFileAttributesW(string lpFileName);
    
    private const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;
    
    /// <summary>
    /// Creates a Windows symbolic link (cross-drive support).
    /// </summary>
    /// <param name="linkPath">Path where the symbolic link will be created</param>
    /// <param name="targetPath">Path to the target file or directory</param>
    /// <param name="isDirectory">True if linking to a directory, false for a file</param>
    /// <returns>Result indicating success or failure</returns>
    public Result CreateSymbolicLink(string linkPath, string targetPath, bool isDirectory)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return Result.Failure("Link path cannot be empty", ErrorCode.InvalidInput);
        }
        
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return Result.Failure("Target path cannot be empty", ErrorCode.InvalidInput);
        }
        
        // Check if target exists
        var targetExists = isDirectory ? Directory.Exists(targetPath) : File.Exists(targetPath);
        if (!targetExists)
        {
            return Result.Failure($"Target does not exist: {targetPath}", ErrorCode.FileNotFound);
        }
        
        // Ensure parent directory of link exists
        var linkDirectory = Path.GetDirectoryName(linkPath);
        if (string.IsNullOrWhiteSpace(linkDirectory))
        {
            return Result.Failure("Invalid link path", ErrorCode.InvalidInput);
        }
        
        if (!Directory.Exists(linkDirectory))
        {
            try
            {
                Directory.CreateDirectory(linkDirectory);
            }
            catch (Exception ex)
            {
                return Result.Failure($"Failed to create directory for symlink: {ex.Message}", ErrorCode.IOError);
            }
        }
        
        // Check if link already exists
        var linkExists = File.Exists(linkPath) || Directory.Exists(linkPath);
        if (linkExists)
        {
            // Check if it's already a symlink pointing to the same target
            if (IsSymbolicLink(linkPath))
            {
                var currentTarget = ResolveSymbolicLink(linkPath);
                if (currentTarget != null && Path.GetFullPath(currentTarget) == Path.GetFullPath(targetPath))
                {
                    return Result.Success(); // Already linked correctly
                }
            }
            
            return Result.Failure($"Path already exists: {linkPath}", ErrorCode.PathConflict);
        }
        
        // Convert paths to absolute paths
        var absoluteLinkPath = Path.GetFullPath(linkPath);
        var absoluteTargetPath = Path.GetFullPath(targetPath);
        
        // Create symbolic link using Windows API
        var flags = isDirectory ? SYMBOLIC_LINK_FLAG_DIRECTORY : SYMBOLIC_LINK_FLAG_FILE;
        
        // Try with unprivileged create flag first (Windows 10 1703+)
        if (!CreateSymbolicLinkW(absoluteLinkPath, absoluteTargetPath, flags | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
        {
            // Fallback to privileged mode
            if (!CreateSymbolicLinkW(absoluteLinkPath, absoluteTargetPath, flags))
            {
                var errorCode = Marshal.GetLastWin32Error();
                var errorMessage = $"Failed to create symbolic link. Error code: {errorCode}";
                
                // Common error codes
                if (errorCode == 1314) // ERROR_PRIVILEGE_NOT_HELD
                {
                    errorMessage += " (Requires administrator privileges or Developer Mode enabled)";
                }
                else if (errorCode == 183) // ERROR_ALREADY_EXISTS
                {
                    errorMessage += " (Path already exists)";
                }
                
                return Result.Failure(errorMessage, ErrorCode.SymlinkCreationFailed);
            }
        }
        
        // Verify the symlink was created successfully
        if (!File.Exists(linkPath) && !Directory.Exists(linkPath))
        {
            return Result.Failure("Symbolic link creation reported success but link does not exist", ErrorCode.SymlinkCreationFailed);
        }
        
        return Result.Success();
    }
    
    /// <summary>
    /// Checks if a path is a symbolic link.
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True if the path is a symbolic link, false otherwise</returns>
    public bool IsSymbolicLink(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }
        
        try
        {
            var attributes = GetFileAttributesW(path);
            return (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Resolves a symbolic link to its target path.
    /// </summary>
    /// <param name="linkPath">Path to the symbolic link</param>
    /// <returns>Target path if successful, null otherwise</returns>
    public string? ResolveSymbolicLink(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return null;
        }
        
        if (!IsSymbolicLink(linkPath))
        {
            return null;
        }
        
        try
        {
            // Use FileInfo/DirectoryInfo to resolve symlinks
            if (File.Exists(linkPath))
            {
                var fileInfo = new FileInfo(linkPath);
                return fileInfo.ResolveLinkTarget(false)?.FullName;
            }
            else if (Directory.Exists(linkPath))
            {
                var dirInfo = new DirectoryInfo(linkPath);
                return dirInfo.ResolveLinkTarget(false)?.FullName;
            }
        }
        catch
        {
            // Ignore exceptions
        }
        
        return null;
    }
    
    /// <summary>
    /// Deletes a symbolic link without deleting the target.
    /// </summary>
    /// <param name="linkPath">Path to the symbolic link</param>
    /// <returns>Result indicating success or failure</returns>
    public Result DeleteSymbolicLink(string linkPath)
    {
        if (string.IsNullOrWhiteSpace(linkPath))
        {
            return Result.Failure("Link path cannot be empty", ErrorCode.InvalidInput);
        }
        
        if (!IsSymbolicLink(linkPath))
        {
            return Result.Failure($"Path is not a symbolic link: {linkPath}", ErrorCode.InvalidInput);
        }
        
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
            }
            else if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath, false);
            }
            else
            {
                return Result.Failure($"Symbolic link does not exist: {linkPath}", ErrorCode.FileNotFound);
            }
            
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to delete symbolic link: {ex.Message}", ErrorCode.IOError);
        }
    }
}

