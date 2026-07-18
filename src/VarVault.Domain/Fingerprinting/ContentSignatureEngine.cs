using System.Buffers.Binary;
using System.Security.Cryptography;
using VarVault.Common;

namespace VarVault.Domain.Fingerprinting;

/// <summary>The three fingerprints computed for a var in one pass. (Data-arch §5.2, checklist BE-F2.)</summary>
public sealed record VarSignatures(
    string ContentSignature,
    string PayloadSignature,
    string ContentSignatureNoPath);

/// <summary>
/// Computes a var's logical fingerprints from its zip central-directory entries — the primary dedup
/// keys. Each is a SHA-256 over a <b>sorted multiset</b> so compression level and entry order don't
/// change the result; directory entries are excluded. Two zips with identical content but different
/// compression or ordering produce the same <see cref="VarSignatures.ContentSignature"/>. (🔒 BE-F1/F2, 1.20–1.22.)
/// </summary>
public static class ContentSignatureEngine
{
    /// <summary>
    /// <b>ContentSignature</b> — sorted multiset of <c>(rawNameBytes, uncompressedSize, CRC-32)</c>;
    /// <b>PayloadSignature</b> — same but excluding root <c>meta.json</c>;
    /// <b>ContentSignatureNoPath</b> — sorted multiset of <c>(uncompressedSize, CRC-32)</c> only (paths excluded).
    /// </summary>
    public static VarSignatures Compute(IReadOnlyList<ZipEntryFacts> entries)
    {
        Guard.NotNull(entries);

        var content = new List<byte[]>();
        var payload = new List<byte[]>();
        var noPath = new List<byte[]>();

        foreach (var e in entries)
        {
            if (e.IsDirectory)
                continue;

            var withPath = EncodeWithPath(e);
            content.Add(withPath);
            if (!e.IsRootMetaJson)
                payload.Add(withPath);
            noPath.Add(EncodeNoPath(e));
        }

        return new VarSignatures(
            HashSortedMultiset(content),
            HashSortedMultiset(payload),
            HashSortedMultiset(noPath));
    }

    // Length-prefixed so arbitrary raw name bytes can't create ambiguous record boundaries.
    private static byte[] EncodeWithPath(ZipEntryFacts e)
    {
        var buffer = new byte[4 + e.RawNameBytes.Length + 8 + 4];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, e.RawNameBytes.Length);
        e.RawNameBytes.CopyTo(span[4..]);
        var rest = span[(4 + e.RawNameBytes.Length)..];
        BinaryPrimitives.WriteInt64LittleEndian(rest, e.UncompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(rest[8..], e.Crc32);
        return buffer;
    }

    private static byte[] EncodeNoPath(ZipEntryFacts e)
    {
        var buffer = new byte[8 + 4];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt64LittleEndian(span, e.UncompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], e.Crc32);
        return buffer;
    }

    private static string HashSortedMultiset(List<byte[]> records)
    {
        records.Sort(ByteArrayComparer.Instance);

        using var sha = SHA256.Create();
        // Include the record count so an empty set has a stable, distinct signature.
        Span<byte> countBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(countBuffer, records.Count);
        sha.TransformBlock(countBuffer.ToArray(), 0, 4, null, 0);

        foreach (var record in records)
            sha.TransformBlock(record, 0, record.Length, null, 0);

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
    }
}
