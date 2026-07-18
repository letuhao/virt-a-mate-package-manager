using System.IO;
using System.IO.Compression;
using VarVault.Common;
using VarVault.Domain.Content;
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
            return new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entries = read.Value;

        var signatures = ContentSignatureEngine.Compute(entries);
        var classification = ContentClassificationEngine.Classify(entries.Select(e => e.DecodedNameBestEffort));
        var encoding = EncodingHealthEngine.Detect(entries);

        var hasMeta = entries.Any(e => e.IsRootMetaJson);
        var meta = hasMeta ? ReadMeta(varPath) : null;
        var integrity = hasMeta ? IntegrityStatus.Ok : IntegrityStatus.MissingMeta;

        return new VarInspection(integrity, entries, signatures, classification, encoding, meta);
    }

    private static VarMeta? ReadMeta(string varPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(varPath);
            var entry = archive.GetEntry("meta.json") ?? FindMetaIgnoreCase(archive);
            if (entry is null)
                return null;

            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            var parsed = VarMetaParser.Parse(text);
            return parsed.IsSuccess ? parsed.Value : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // Central directory said meta.json exists but the payload can't be decompressed — rare.
            return null;
        }
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
