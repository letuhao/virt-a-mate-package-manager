using System.Text;

namespace VarVault.Domain.Fingerprinting;

/// <summary>
/// The identity-relevant facts of one zip central-directory entry, captured with the
/// <b>raw (undecoded) filename bytes</b>. Working from raw bytes is load-bearing: a heavily-CJK
/// library contains mojibake var names where decoding is lossy, so the dedup signature must not
/// depend on any codepage guess. (Data-arch §5.2.)
/// </summary>
public sealed record ZipEntryFacts(
    byte[] RawNameBytes,
    long UncompressedSize,
    uint Crc32,
    bool IsDirectory)
{
    private static readonly byte[] MetaJsonUtf8 = "meta.json"u8.ToArray();

    /// <summary>True if this entry is the root <c>meta.json</c> (ASCII; excluded from the payload signature).</summary>
    public bool IsRootMetaJson =>
        RawNameBytes.Length == MetaJsonUtf8.Length && AsciiEqualsIgnoreCase(RawNameBytes, MetaJsonUtf8);

    /// <summary>Best-effort UTF-8 decode of the name for display/logging only — never for identity.</summary>
    public string DecodedNameBestEffort => Encoding.UTF8.GetString(RawNameBytes);

    private static bool AsciiEqualsIgnoreCase(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            var ca = a[i];
            var cb = b[i];
            if (ca is >= (byte)'A' and <= (byte)'Z') ca += 32;
            if (cb is >= (byte)'A' and <= (byte)'Z') cb += 32;
            if (ca != cb)
                return false;
        }
        return true;
    }
}
