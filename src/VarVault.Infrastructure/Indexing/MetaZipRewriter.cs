using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Rewrites a <c>.var</c> zip with an updated <c>meta.json</c>, validates it for VaM, and writes to
/// a sibling <c>.partial</c> then the destination path. Never mutates the source in place.
/// </summary>
public static class MetaZipRewriter
{
    public static async Task<Result> RewriteAsync(
        string sourcePath,
        string destinationPath,
        string newMetaJson,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(sourcePath);
        Guard.NotNullOrWhiteSpace(destinationPath);
        Guard.NotNull(newMetaJson);

        if (!File.Exists(sourcePath))
            return Result.Failure("meta.missing", $"Source not found: {sourcePath}");

        var tempPath = destinationPath + ".partial";
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read))
            await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var destZip = new ZipArchive(destStream, ZipArchiveMode.Create))
            {
                var wroteMeta = false;
                foreach (var entry in sourceZip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/'))
                        continue;

                    var isMeta = string.Equals(entry.FullName, "meta.json", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(Path.GetFileName(entry.FullName), "meta.json", StringComparison.OrdinalIgnoreCase)
                                    && !entry.FullName.Contains('/', StringComparison.Ordinal)
                                    && !entry.FullName.Contains('\\', StringComparison.Ordinal);

                    var newEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    await using var output = newEntry.Open();
                    if (isMeta)
                    {
                        var bytes = Encoding.UTF8.GetBytes(newMetaJson);
                        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                        wroteMeta = true;
                    }
                    else
                    {
                        await using var input = entry.Open();
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (!wroteMeta)
                {
                    var metaEntry = destZip.CreateEntry("meta.json", CompressionLevel.Optimal);
                    await using var output = metaEntry.Open();
                    var bytes = Encoding.UTF8.GetBytes(newMetaJson);
                    await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }
            }

            var read = ZipCentralDirectoryReader.Read(tempPath);
            if (read.IsFailure)
            {
                TryDelete(tempPath);
                return Result.Failure("meta.invalid", "rewritten var is not a readable zip");
            }

            var validation = VamVarValidator.Validate(read.Value);
            if (validation.IsFailure)
            {
                TryDelete(tempPath);
                return validation;
            }

            if (File.Exists(destinationPath))
                File.Delete(destinationPath);
            File.Move(tempPath, destinationPath);
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            TryDelete(tempPath);
            return Result.Failure("meta.io", ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
