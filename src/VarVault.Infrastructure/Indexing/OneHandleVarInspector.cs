using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// One seekable handle: central directory + meta/embedded refs + optional representative preview.
/// Enforces <see cref="IngestLimits"/> so huge entries cannot OOM the worker. (A13/A14.)
/// </summary>
public sealed class OneHandleVarInspector
{
    private readonly PreviewExtractor _previews;

    public OneHandleVarInspector(PreviewExtractor previews) => _previews = previews;

    public sealed record Result(
        VarInspection Inspection,
        byte[]? RepresentativeThumbJpeg,
        string? RepresentativeEntryPath);

    public Result Inspect(string varPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(varPath);
        if (!File.Exists(varPath))
            return new Result(new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null, []), null, null);

        try
        {
            // Single seekable handle for the whole inspection pass (A13).
            using var stream = new FileStream(
                varPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.RandomAccess);

            var cd = ZipCentralDirectoryReader.Read(stream);
            if (cd.IsFailure)
                return new Result(new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null, []), null, null);

            cancellationToken.ThrowIfCancellationRequested();
            var entries = cd.Value;
            if (entries.Count == 0)
                return new Result(new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null, []), null, null);

            // Fingerprints/classification come from the CD — keep them even if ZipArchive fails
            // (ArgumentException is often duplicate FullNames, not a dead zip).
            var signatures = ContentSignatureEngine.Compute(entries);
            var classification = ContentClassificationEngine.Classify(entries.Select(e => e.DecodedNameBestEffort));
            var encoding = EncodingHealthEngine.Detect(entries);
            var hasMeta = entries.Any(e => e.IsRootMetaJson);

            VarMeta? meta = null;
            var embedded = new List<string>();
            byte[]? thumb = null;
            string? thumbEntry = null;
            var crcMismatch = false;

            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

                if (!ZipCrcSpotChecker.TryVerify(archive, out _))
                    crcMismatch = true;

                if (hasMeta)
                {
                    var metaEntry = archive.GetEntry("meta.json")
                        ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, "meta.json", StringComparison.OrdinalIgnoreCase));
                    if (metaEntry is not null)
                    {
                        var text = ReadCapped(metaEntry, IngestLimits.MaxJsonEntryBytes);
                        if (text is not null)
                        {
                            var parsed = VarMetaParser.Parse(text);
                            if (parsed.IsSuccess)
                                meta = parsed.Value;
                        }
                    }
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsEmbeddedJson(entry.FullName))
                        continue;
                    var text = ReadCapped(entry, IngestLimits.MaxJsonEntryBytes);
                    if (text is null)
                        continue;
                    foreach (var reference in EmbeddedRefExtractor.Extract(text))
                    {
                        if (seen.Add(reference))
                            embedded.Add(reference);
                    }
                }

                // Representative preview in the same open (A14).
                var primary = ContentClassificationEngine.ChoosePrimary(classification.Counts);
                if (PreviewRules.HasPreview(primary))
                {
                    foreach (var item in classification.Items.Where(i => i.Type == primary || PreviewRules.HasPreview(i.Type)))
                    {
                        var sibling = PreviewRules.SiblingJpgPath(item.EntryPath);
                        var jpg = archive.GetEntry(sibling)
                            ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, sibling, StringComparison.OrdinalIgnoreCase));
                        if (jpg is null || jpg.Length > IngestLimits.MaxPreviewCompressedBytes)
                            continue;
                        using var jpgStream = jpg.Open();
                        // Allocate exactly once. MemoryStream + ToArray duplicated every preview's
                        // large byte buffer on the LOH and caused avoidable Gen2 collections.
                        var raw = ReadBytesCapped(jpgStream, jpg.Length, IngestLimits.MaxPreviewCompressedBytes);
                        thumb = PreviewExtractor.Downscale(raw);
                        thumbEntry = item.EntryPath;
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                // Soft failure after a good CD: do not invent CorruptZip / wipe fingerprints.
                // ArgumentException is commonly duplicate FullName keys (VaM-load issue, fixable).
            }

            var integrity = VarInspector.DeriveIntegrity(entries, hasMeta, crcMismatch);
            var inspection = new VarInspection(integrity, entries, signatures, classification, encoding, meta, embedded);
            return new Result(inspection, thumb, thumbEntry);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            // File open / unexpected before CD — truly unreadable.
            return new Result(new VarInspection(IntegrityStatus.CorruptZip, [], null, null, null, null, []), null, null);
        }
    }

    private static bool IsEmbeddedJson(string entryName)
    {
        if (string.Equals(entryName, "meta.json", StringComparison.OrdinalIgnoreCase))
            return false;
        return entryName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".vap", StringComparison.OrdinalIgnoreCase)
            || entryName.EndsWith(".vac", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadCapped(ZipArchiveEntry entry, long maxBytes)
    {
        if (entry.Length > maxBytes)
            return null;
        using var stream = entry.Open();
        using var limited = new LimitedReadStream(stream, maxBytes);
        using var reader = new StreamReader(limited, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytesCapped(Stream source, long expectedBytes, long maxBytes)
    {
        if (expectedBytes < 0 || expectedBytes > maxBytes || expectedBytes > int.MaxValue)
            throw new InvalidDataException("entry exceeds ingest cap");
        var bytes = new byte[(int)expectedBytes];
        var read = 0;
        while (read < bytes.Length)
        {
            var count = source.Read(bytes, read, bytes.Length - read);
            if (count == 0)
                throw new InvalidDataException("truncated preview entry");
            read += count;
        }
        return bytes;
    }

    private sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_read >= maxBytes)
                return 0;
            var allowed = (int)Math.Min(count, maxBytes - _read);
            var n = inner.Read(buffer, offset, allowed);
            _read += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
