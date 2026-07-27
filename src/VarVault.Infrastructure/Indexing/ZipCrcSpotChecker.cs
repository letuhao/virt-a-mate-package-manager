using System.IO;
using System.IO.Compression;
using VarVault.Common;
using VarVault.Common.Hashing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// IDX-10 · Spot-check entry payload CRC against the central-directory CRC so truncated/corrupt
/// downloads with a still-readable CD are flagged <c>CorruptZip</c>.
/// </summary>
public static class ZipCrcSpotChecker
{
    /// <summary>Cap per-entry decompress for inspect latency (large morphs still covered by CD facts).</summary>
    public const long MaxEntryBytes = 8L * 1024 * 1024;

    /// <summary>meta.json + up to this many additional file entries.</summary>
    public const int MaxSampleEntries = 4;

    /// <summary>
    /// Returns <c>false</c> when a sampled entry's uncompressed payload CRC ≠ CD CRC.
    /// </summary>
    public static bool TryVerify(ZipArchive archive, out string? detail)
    {
        Guard.NotNull(archive);
        detail = null;
        Span<byte> buffer = stackalloc byte[8192];
        foreach (var entry in PickSamples(archive))
        {
            if (entry.Length < 0)
                continue;
            if (entry.Length > MaxEntryBytes)
                continue;

            try
            {
                using var stream = entry.Open();
                var actual = Crc32Ieee.Compute(stream, buffer);
                var expected = unchecked((uint)entry.Crc32);
                if (actual != expected)
                {
                    detail =
                        $"CRC mismatch for '{entry.FullName}': payload 0x{actual:X8} ≠ CD 0x{expected:X8}";
                    return false;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                detail = $"CRC spot-check failed for '{entry.FullName}': {ex.Message}";
                return false;
            }
        }

        return true;
    }

    private static List<ZipArchiveEntry> PickSamples(ZipArchive archive)
    {
        var files = archive.Entries
            .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
            .ToList();

        var samples = new List<ZipArchiveEntry>(MaxSampleEntries);
        var meta = files.FirstOrDefault(e =>
            string.Equals(e.FullName, "meta.json", StringComparison.OrdinalIgnoreCase));
        if (meta is not null)
            samples.Add(meta);

        foreach (var e in files)
        {
            if (samples.Count >= MaxSampleEntries)
                break;
            if (ReferenceEquals(e, meta))
                continue;
            if (e.Length > MaxEntryBytes)
                continue;
            samples.Add(e);
        }

        return samples;
    }
}
