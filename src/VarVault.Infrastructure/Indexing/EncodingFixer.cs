using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Common;
using VarVault.Domain.Content;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Re-encodes a broken-encoding var to UTF-8. Reads the source with the detected codepage (so entry
/// names decode correctly), writes a NEW zip whose names are UTF-8 (bit 11 set) with Deflate, validates
/// it against VaM's constraints, then atomically renames it into place. The original is never touched.
/// (IDX-9; checklist 4.12/4.13.)
/// </summary>
public sealed class EncodingFixer : IEncodingFixer
{
    static EncodingFixer() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<Result<EncodingFixOutcome>> FixAsync(
        string sourcePath,
        int codePage,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(sourcePath);
        Guard.NotNullOrWhiteSpace(outputPath);

        if (!File.Exists(sourcePath))
            return Result.Failure<EncodingFixOutcome>("fix.missing", $"Source not found: {sourcePath}");
        if (File.Exists(outputPath))
            return Result.Failure<EncodingFixOutcome>("fix.exists", $"Output already exists: {outputPath}");

        var tempPath = outputPath + ".partial";
        var written = 0;
        try
        {
            var sourceEncoding = Encoding.GetEncoding(codePage);

            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false, sourceEncoding))
            await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
            using (var destZip = new ZipArchive(destStream, ZipArchiveMode.Create)) // UTF-8 names by default
            {
                foreach (var entry in sourceZip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith('/'))
                        continue; // directory entries are implicit in the rewrite

                    var newEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    await using var input = entry.Open();
                    await using var output = newEntry.Open();
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    written++;
                }
            }

            // Validate the rewrite against VaM's load constraints before preferring it. (4.13)
            var read = ZipCentralDirectoryReader.Read(tempPath);
            if (read.IsFailure)
                return Fail(tempPath, "fix.invalid", "rewritten var is not a readable zip");

            var validation = VamVarValidator.Validate(read.Value);
            if (validation.IsFailure)
                return Fail(tempPath, validation.Error.Code, validation.Error.Message);

            // Atomic rename — never overwrite in place. (4.12)
            File.Move(tempPath, outputPath);
            return new EncodingFixOutcome(outputPath, written);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            TryDelete(tempPath);
            return Result.Failure<EncodingFixOutcome>("fix.io", ex.Message);
        }
    }

    private static Result<EncodingFixOutcome> Fail(string tempPath, string code, string message)
    {
        TryDelete(tempPath);
        return Result.Failure<EncodingFixOutcome>(code, message);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* best effort */ }
    }
}
