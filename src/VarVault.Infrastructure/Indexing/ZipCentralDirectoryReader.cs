using System.Buffers.Binary;
using System.IO;
using VarVault.Common;
using VarVault.Domain.Fingerprinting;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Reads a zip's central directory directly (not via <c>ZipArchive</c>, which decodes names and would
/// lose the raw bytes needed for mojibake-safe fingerprinting). Zip64-aware; directory entries kept
/// (the fingerprint engine excludes them). A structurally invalid archive yields a
/// <c>zip.corrupt</c> failure so the indexer can mark it <c>CorruptZip</c>. (🔒 BE-F1, checklist 1.20/1.27.)
/// </summary>
public static class ZipCentralDirectoryReader
{
    private const uint EocdSignature = 0x06054b50;
    private const uint Zip64EocdLocatorSignature = 0x07064b50;
    private const uint Zip64EocdSignature = 0x06064b50;
    private const uint CentralHeaderSignature = 0x02014b50;
    private const int EocdMinSize = 22;
    private const int MaxCommentSize = 0xFFFF;

    public static Result<IReadOnlyList<ZipEntryFacts>> Read(string path)
    {
        Guard.NotNullOrWhiteSpace(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream);
        }
        catch (FileNotFoundException)
        {
            return Result.Failure<IReadOnlyList<ZipEntryFacts>>("zip.missing", $"File not found: {path}");
        }
        catch (IOException ex)
        {
            return Result.Failure<IReadOnlyList<ZipEntryFacts>>("zip.io", ex.Message);
        }
    }

    public static Result<IReadOnlyList<ZipEntryFacts>> Read(Stream stream)
    {
        Guard.NotNull(stream);
        try
        {
            return ReadCore(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException or InvalidDataException)
        {
            // Any structural surprise in a malformed archive is a corruption, not a crash.
            return Corrupt(ex.Message);
        }
    }

    private static Result<IReadOnlyList<ZipEntryFacts>> ReadCore(Stream stream)
    {
        var length = stream.Length;
        if (length < EocdMinSize)
            return Corrupt("file too small to be a zip");

        // 1. Locate + read the EOCD by scanning a tail window backwards for its signature.
        var tailSize = (int)Math.Min(length, EocdMinSize + MaxCommentSize);
        var tail = ReadAt(stream, length - tailSize, tailSize);
        var eocdInTail = FindLastSignature(tail, EocdSignature);
        if (eocdInTail < 0)
            return Corrupt("end-of-central-directory record not found");

        var eocd = tail.AsSpan(eocdInTail);
        long totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);
        var eocdAbsolute = length - tailSize + eocdInTail;

        // 2. Zip64 escape when any field is saturated.
        if (totalEntries == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
        {
            var z64 = TryReadZip64(stream, tail, eocdInTail, length - tailSize);
            if (z64.IsFailure)
                return Result.Failure<IReadOnlyList<ZipEntryFacts>>(z64.Error);
            (totalEntries, cdSize, cdOffset) = z64.Value;
        }

        if (cdSize <= 0 || cdSize > length)
            return Corrupt("central-directory size out of range");

        // 3. Read the central directory. Fall back to a computed start if the recorded offset is
        //    shifted (e.g. archive with prepended data), which is common and still valid.
        var cdStart = cdOffset;
        if (cdStart < 0 || cdStart + cdSize > eocdAbsolute)
            cdStart = eocdAbsolute - cdSize;
        if (cdStart < 0)
            return Corrupt("central-directory offset out of range");

        var cd = ReadAt(stream, cdStart, (int)cdSize);
        if (cd.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(cd) != CentralHeaderSignature)
        {
            // Retry with the recorded offset if the fallback guessed wrong.
            if (cdOffset >= 0 && cdOffset + cdSize <= length)
                cd = ReadAt(stream, cdOffset, (int)cdSize);
        }

        var parsed = ParseCentralDirectory(cd);
        return parsed.IsFailure
            ? Result.Failure<IReadOnlyList<ZipEntryFacts>>(parsed.Error)
            : Result.Success<IReadOnlyList<ZipEntryFacts>>(parsed.Value);
    }

    private static Result<(long entries, long cdSize, long cdOffset)> TryReadZip64(
        Stream stream, byte[] tail, int eocdInTail, long tailBase)
    {
        // The Zip64 EOCD locator sits 20 bytes before the EOCD.
        var locatorInTail = eocdInTail - 20;
        if (locatorInTail < 0)
            return Result.Failure<(long, long, long)>("zip.corrupt", "zip64 locator missing");

        var locator = tail.AsSpan(locatorInTail);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64EocdLocatorSignature)
            return Result.Failure<(long, long, long)>("zip.corrupt", "zip64 locator signature mismatch");

        var z64EocdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        if (z64EocdOffset < 0 || z64EocdOffset + 56 > tailBase + tail.Length)
            return Result.Failure<(long, long, long)>("zip.corrupt", "zip64 EOCD offset out of range");

        var z64 = ReadAt(stream, z64EocdOffset, 56);
        if (BinaryPrimitives.ReadUInt32LittleEndian(z64) != Zip64EocdSignature)
            return Result.Failure<(long, long, long)>("zip.corrupt", "zip64 EOCD signature mismatch");

        var entries = (long)BinaryPrimitives.ReadUInt64LittleEndian(z64.AsSpan(32));
        var cdSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(z64.AsSpan(40));
        var cdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(z64.AsSpan(48));
        return Result.Success((entries, cdSize, cdOffset));
    }

    private static Result<IReadOnlyList<ZipEntryFacts>> ParseCentralDirectory(byte[] cd)
    {
        var entries = new List<ZipEntryFacts>();
        var pos = 0;

        while (pos + 46 <= cd.Length)
        {
            var span = cd.AsSpan(pos);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != CentralHeaderSignature)
                break; // reached the end / padding

            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
            long uncompressed = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]);
            int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(span[32..]);

            var recordLen = 46 + nameLen + extraLen + commentLen;
            if (pos + recordLen > cd.Length)
                return Corrupt("truncated central-directory record");

            var rawName = span.Slice(46, nameLen).ToArray();

            if (uncompressed == 0xFFFFFFFF && extraLen > 0)
                uncompressed = ReadZip64Uncompressed(span.Slice(46 + nameLen, extraLen), uncompressed);

            var isDirectory = nameLen > 0 && rawName[^1] == (byte)'/';
            entries.Add(new ZipEntryFacts(rawName, uncompressed, crc, isDirectory));

            pos += recordLen;
        }

        return entries.Count == 0
            ? Corrupt("no central-directory entries")
            : Result.Success<IReadOnlyList<ZipEntryFacts>>(entries);
    }

    // The Zip64 extended-info extra block (id 0x0001) carries the true uncompressed size first
    // (only for the fields that were saturated to 0xFFFFFFFF in the fixed record).
    private static long ReadZip64Uncompressed(ReadOnlySpan<byte> extra, long fallback)
    {
        var p = 0;
        while (p + 4 <= extra.Length)
        {
            var headerId = BinaryPrimitives.ReadUInt16LittleEndian(extra[p..]);
            var blockSize = BinaryPrimitives.ReadUInt16LittleEndian(extra[(p + 2)..]);
            var dataStart = p + 4;
            if (dataStart + blockSize > extra.Length)
                break;
            if (headerId == 0x0001 && blockSize >= 8)
                return (long)BinaryPrimitives.ReadUInt64LittleEndian(extra[dataStart..]);
            p = dataStart + blockSize;
        }
        return fallback;
    }

    private static int FindLastSignature(byte[] buffer, uint signature)
    {
        for (var i = buffer.Length - 4; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i)) == signature)
                return i;
        }
        return -1;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
                break;
            read += n;
        }
        return read == count ? buffer : buffer[..read];
    }

    private static Result<IReadOnlyList<ZipEntryFacts>> Corrupt(string reason) =>
        Result.Failure<IReadOnlyList<ZipEntryFacts>>("zip.corrupt", $"Corrupt zip: {reason}");
}
