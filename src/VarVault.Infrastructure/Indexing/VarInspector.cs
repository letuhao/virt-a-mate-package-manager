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
        var (meta, embeddedRefs) = ReadContent(varPath, hasMeta);
        var integrity = hasMeta ? IntegrityStatus.Ok : IntegrityStatus.MissingMeta;

        return new VarInspection(integrity, entries, signatures, classification, encoding, meta, embeddedRefs);
    }

    // One archive pass: parse meta.json AND harvest embedded package refs from scene/preset JSON. (2.1)
    private static (VarMeta? Meta, IReadOnlyList<string> EmbeddedRefs) ReadContent(string varPath, bool hasMeta)
    {
        VarMeta? meta = null;
        var embedded = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = ZipFile.OpenRead(varPath);

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
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // Content unreadable — return what we have (fingerprints/classification already captured).
        }

        return (meta, embedded);
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
