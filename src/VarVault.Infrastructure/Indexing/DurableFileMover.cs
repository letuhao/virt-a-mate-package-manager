using System.IO;
using System.Security.Cryptography;
using VarVault.Common;
using VarVault.Domain.Migration;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Durable file move: copy → flush (WriteThrough) → verify by re-reading the destination and comparing
/// its full hash to the source → atomic rename. Leaves the source intact for the caller to delete last.
/// (Data-arch §5.6; checklist 5.8/5.13.)
/// </summary>
public sealed class DurableFileMover : IDurableFileMover
{
    private const int BufferSize = 1 << 20;

    public async Task<Result<DurableCopyOutcome>> CopyVerifyRenameAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(sourcePath);
        Guard.NotNullOrWhiteSpace(destinationPath);

        if (!File.Exists(sourcePath))
            return Result.Failure<DurableCopyOutcome>("move.missing", $"Source not found: {sourcePath}");
        if (File.Exists(destinationPath))
            return Result.Failure<DurableCopyOutcome>("move.exists", $"Destination already exists: {destinationPath}");

        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        var tempPath = destinationPath + ".partial";
        try
        {
            var sourceHash = await HashAsync(sourcePath, cancellationToken).ConfigureAwait(false);

            // Copy source → temp, flushing through the OS write cache.
            await using (var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true); // FlushFileBuffers
            }

            // Verify: re-read the destination and compare the full hash. (Never delete on a heuristic.)
            var destHash = await HashAsync(tempPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(sourceHash, destHash, StringComparison.Ordinal))
            {
                TryDelete(tempPath);
                return Result.Failure<DurableCopyOutcome>("move.verify", "copied file did not verify against the source");
            }

            // Atomic rename — the destination appears only once fully written and verified. (5.13)
            File.Move(tempPath, destinationPath);
            return new DurableCopyOutcome(destinationPath, destHash);
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath); // interrupted → only a .partial ever existed
            throw;
        }
        catch (IOException ex)
        {
            TryDelete(tempPath);
            return Result.Failure<DurableCopyOutcome>("move.io", ex.Message);
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort */ }
    }
}
