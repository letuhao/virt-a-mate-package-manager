using System.IO;
using System.IO.Compression;
using VarVault.Common;
using VarVault.Domain.Content;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Duplicate-entry repair: for each VaM-normalized path key, retain one member (per-key choice or
/// keep-larger) and drop the rest into a new <c>.dedup.var</c>. Validates with <see cref="VamLoadDefectDetector"/>.
/// </summary>
public sealed class DuplicateEntryFixer : IDuplicateEntryFixer
{
    public async Task<Result<DuplicateEntryFixOutcome>> FixAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(sourcePath);
        Guard.NotNullOrWhiteSpace(outputPath);

        if (!File.Exists(sourcePath))
            return Result.Failure<DuplicateEntryFixOutcome>("dedup.missing", $"Source not found: {sourcePath}");
        if (File.Exists(outputPath))
            return Result.Failure<DuplicateEntryFixOutcome>("dedup.exists", $"Output already exists: {outputPath}");

        var tempPath = outputPath + ".partial";
        var kept = 0;
        var dropped = 0;
        try
        {
            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                var fileEntries = sourceZip.Entries
                    .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
                    .ToList();

                var winners = SelectWinners(fileEntries, keepByNormalizedKey);
                if (winners.IsFailure)
                    return Result.Failure<DuplicateEntryFixOutcome>(winners.Error);

                dropped = fileEntries.Count - winners.Value.Count;

                await using (var destStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                using (var destZip = new ZipArchive(destStream, ZipArchiveMode.Create))
                {
                    foreach (var entry in winners.Value)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var newEntry = destZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                        await using var input = entry.Open();
                        await using var output = newEntry.Open();
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        kept++;
                    }
                }
            }

            var read = ZipCentralDirectoryReader.Read(tempPath);
            if (read.IsFailure)
                return Fail(tempPath, "dedup.invalid", "rewritten var is not a readable zip");

            var remaining = VamLoadDefectDetector.Detect(read.Value)
                .Where(d => d.Kind == VamLoadDefectKind.DuplicateEntries)
                .ToList();
            if (remaining.Count > 0)
                return Fail(tempPath, "dedup.stilldup", remaining[0].Detail);

            // meta.json must survive (VaM load).
            if (!read.Value.Any(e => e.IsRootMetaJson))
                return Fail(tempPath, "dedup.meta", "meta.json was dropped during dedup");

            File.Move(tempPath, outputPath);
            return new DuplicateEntryFixOutcome(outputPath, kept, dropped);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            TryDelete(tempPath);
            return Result.Failure<DuplicateEntryFixOutcome>("dedup.io", ex.Message);
        }
    }

    /// <summary>
    /// Per normalized key: honor an explicit FullName choice (Ordinal), else keep the largest entry;
    /// ties keep first occurrence. Non-colliding keys keep their sole member.
    /// </summary>
    internal static Result<List<ZipArchiveEntry>> SelectWinners(
        IReadOnlyList<ZipArchiveEntry> fileEntries,
        IReadOnlyDictionary<string, string>? keepByNormalizedKey = null)
    {
        var byKey = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in fileEntries)
        {
            var key = VamLoadDefectDetector.NormalizeEntryKey(entry.FullName);
            if (string.IsNullOrEmpty(key))
                continue;
            if (!byKey.TryGetValue(key, out var list))
            {
                list = [];
                byKey[key] = list;
            }

            list.Add(entry);
        }

        var best = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, members) in byKey)
        {
            if (keepByNormalizedKey is not null
                && keepByNormalizedKey.TryGetValue(key, out var chosen)
                && !string.IsNullOrEmpty(chosen))
            {
                var match = members.FirstOrDefault(m =>
                        string.Equals(m.FullName, chosen, StringComparison.Ordinal))
                    ?? members.FirstOrDefault(m =>
                        string.Equals(m.FullName, chosen, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    return Result.Failure<List<ZipArchiveEntry>>(
                        "dedup.choice",
                        $"No entry FullName '{chosen}' under key '{key}'.");
                }

                best[key] = match;
                continue;
            }

            var winner = members[0];
            for (var i = 1; i < members.Count; i++)
            {
                if (members[i].Length > winner.Length)
                    winner = members[i];
            }

            best[key] = winner;
        }

        // Preserve original zip order among winners.
        var winnerSet = best.Values.ToHashSet();
        return fileEntries.Where(winnerSet.Contains).ToList();
    }

    private static Result<DuplicateEntryFixOutcome> Fail(string tempPath, string code, string message)
    {
        TryDelete(tempPath);
        return Result.Failure<DuplicateEntryFixOutcome>(code, message);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
