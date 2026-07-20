using System.IO;
using VarVault.Common;
using VarVault.Domain.Activation;

namespace VarVault.Infrastructure.Activation;

/// <summary>
/// Windows symlink implementation. Link creation needs Developer Mode (or elevation); a privilege
/// failure returns a clear, actionable error instead of throwing. Repoint deletes only the link, never
/// the target's contents. (Data-arch §5.7; checklist 3.2/3.3.)
/// </summary>
public sealed class SymlinkService : ISymlinkService
{
    private const string DevModeHint =
        "Creating symlinks requires Windows Developer Mode (Settings → Privacy & security → For developers) or running elevated.";

    // ERROR_PRIVILEGE_NOT_HELD (1314) surfaces as an IOException with this HResult on Windows.
    private const int HResultPrivilegeNotHeld = unchecked((int)0x80070522);

    public Result CreateDirectory(string linkPath, string targetPath)
    {
        Guard.NotNullOrWhiteSpace(linkPath);
        Guard.NotNullOrWhiteSpace(targetPath);
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Classify(ex);
        }
    }

    public Result CreateFile(string linkPath, string targetPath)
    {
        Guard.NotNullOrWhiteSpace(linkPath);
        Guard.NotNullOrWhiteSpace(targetPath);
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Classify(ex);
        }
    }

    private static Result Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Result.Failure("symlink.privilege", DevModeHint),
        IOException io when io.HResult == HResultPrivilegeNotHeld => Result.Failure("symlink.privilege", DevModeHint),
        IOException io => Result.Failure("symlink.io", io.Message),
        _ => Result.Failure("symlink.io", ex.Message),
    };

    public Result RepointDirectory(string linkPath, string newTargetPath)
    {
        Guard.NotNullOrWhiteSpace(linkPath);
        Guard.NotNullOrWhiteSpace(newTargetPath);

        try
        {
            if (Directory.Exists(linkPath) || File.Exists(linkPath))
            {
                if (!IsLink(linkPath))
                    return Result.Failure("symlink.notalink", $"'{linkPath}' exists and is not a symlink; refusing to replace it.");
                // Deleting a directory symlink removes the link only, never the target's contents.
                Directory.Delete(linkPath);
            }

            Directory.CreateSymbolicLink(linkPath, newTargetPath);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Classify(ex);
        }
    }

    public Result DeleteLink(string linkPath)
    {
        Guard.NotNullOrWhiteSpace(linkPath);
        try
        {
            var isDir = Directory.Exists(linkPath);
            var isFile = File.Exists(linkPath);
            if (!isDir && !isFile)
                return Result.Success(); // idempotent: nothing to delete

            if (!IsLink(linkPath))
                return Result.Failure("symlink.notalink", $"'{linkPath}' exists and is not a symlink; refusing to delete it.");

            // Deleting a symlink removes the link only, never the target's contents.
            if (isDir)
                Directory.Delete(linkPath);
            else
                File.Delete(linkPath);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Classify(ex);
        }
    }

    public string? ResolveTarget(string linkPath)
    {
        Guard.NotNullOrWhiteSpace(linkPath);
        try
        {
            var dir = new DirectoryInfo(linkPath);
            if (dir.Exists)
                return dir.LinkTarget;
            var file = new FileInfo(linkPath);
            return file.Exists ? file.LinkTarget : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public bool IsLink(string path)
    {
        Guard.NotNullOrWhiteSpace(path);
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
