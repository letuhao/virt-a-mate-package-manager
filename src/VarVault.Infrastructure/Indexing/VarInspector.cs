using System.IO;
using System.IO.Compression;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Opens a var once and produces a full <see cref="VarInspection"/>: reads the central directory,
/// computes the three fingerprints, classifies content, detects encoding health, and reads/parses
/// <c>meta.json</c>. Integrity is derived (CorruptZip / MissingMeta / Ok). (IDX-1/5/6/8/10.)
/// </summary>
public sealed class VarInspector : IVarInspector
{
    public Result<VarInspection> Inspect(string varPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(varPath);
        if (!File.Exists(varPath))
            return Result.Failure<VarInspection>("var.missing", $"File not found: {varPath}");

        var read = ZipCentralDirectoryReader.Read(varPath);
        if (read.IsFailure)
        {
            // Structurally unreadable → corrupt, but not an inspection *failure*.
            return new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null, []);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = read.Value;

        var signatures = ContentSignatureEngine.Compute(entries);
        var classification = ContentClassificationEngine.Classify(entries.Select(e => e.DecodedNameBestEffort));
        var encoding = EncodingHealthEngine.Detect(entries);

        var hasMeta = entries.Any(e => e.IsRootMetaJson);
        var (meta, embeddedRefs, crcMismatch) = ReadContent(varPath, hasMeta);
        var integrity = DeriveIntegrity(entries, hasMeta, crcMismatch);

        return new VarInspection(integrity, entries, signatures, classification, encoding, meta, embeddedRefs);
    }

    /// <summary>
    /// CorruptZip (CRC spot-check) &gt; MissingMeta &gt; DuplicateEntries &gt; Ok.
    /// </summary>
    internal static IntegrityStatus DeriveIntegrity(
        IReadOnlyList<ZipEntryFacts> entries,
        bool hasMeta,
        bool crcMismatch = false)
    {
        if (crcMismatch)
            return IntegrityStatus.CorruptZip;
        if (!hasMeta)
            return IntegrityStatus.MissingMeta;
        var dups = VamLoadDefectDetector.Detect(entries)
            .Any(d => d.Kind == VamLoadDefectKind.DuplicateEntries);
        return dups ? IntegrityStatus.DuplicateEntries : IntegrityStatus.Ok;
    }

    // One archive pass: parse meta.json, harvest embedded refs, CRC spot-check. (2.1 / IDX-10)
    private static (VarMeta? Meta, IReadOnlyList<string> EmbeddedRefs, bool CrcMismatch) ReadContent(
        string varPath,
        bool hasMeta)
    {
        VarMeta? meta = null;
        var embedded = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var crcMismatch = false;

        try
        {
            using var archive = ZipFile.OpenRead(varPath);

            if (!ZipCrcSpotChecker.TryVerify(archive, out _))
                crcMismatch = true;

            if (hasMeta)
            {
                var metaEntry = archive.GetEntry("meta.json") ?? FindMetaIgnoreCase(archive);
                if (metaEntry is not null)
                {
                    var parsed = VarMetaParser.Parse(ReadEntry(metaEntry));
                    if (parsed.IsSuccess)
                        meta = parsed.Value;
                }
            }

            foreach (var entry in archive.Entries)
            {
                if (!IsEmbeddedJson(entry.FullName))
                    continue;
                foreach (var reference in EmbeddedRefExtractor.Extract(ReadEntry(entry)))
                {
                    if (seen.Add(reference))
                        embedded.Add(reference);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            // Content unreadable — return fingerprints/classification; CRC may be unverified.
            // Do not invent CorruptZip here (ArgumentException is often duplicate FullNames).
        }

        return (meta, embedded, crcMismatch);
    }

    private static bool IsEmbeddedJson(string entryName)
    {
        if (string.Equals(entryName, "meta.json", StringComparison.OrdinalIgnoreCase))
            return false;
        return entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".vac", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ZipArchiveEntry? FindMetaIgnoreCase(ZipArchive archive)
    {
        foreach (var e in archive.Entries)
        {
            if (string.Equals(e.FullName, "meta.json", StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }
}
