using System.Text;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;

namespace VarVault.Domain.Content;

/// <summary>Outcome of encoding-health detection for a var. (IDX-8, checklist 4.10.)</summary>
public sealed record EncodingHealthResult(
    EncodingHealth Health,
    string? DetectedCodepage,
    int BrokenEntryCount);

/// <summary>
/// Detects vars whose zip entry names are in a legacy CJK codepage without the UTF-8 flag — the ones
/// VaM fails to load. Per entry: skip if the UTF-8 flag is set or the name is pure ASCII; otherwise
/// try valid-UTF-8 first (so healthy UTF-8-without-flag vars aren't false-flagged), then GBK / GB18030
/// / Shift-JIS / Big5 / EUC-KR with strict round-trip validation, voting on the codepage. A superset
/// of the Boss963 tool (which hardcoded GB2312) — it auto-detects and records which codepage. (IDX-8.)
/// </summary>
public static class EncodingHealthEngine
{
    // (code page, display name) in detection priority. GBK first (most common in the CJK VaM
    // community); GB18030 last because it is nearly a full Unicode map and would over-claim as a
    // catch-all if tried earlier.
    private static readonly (int CodePage, string Name)[] Candidates =
    [
        (936, "GBK"),
        (932, "Shift-JIS"),
        (950, "Big5"),
        (949, "EUC-KR"),
        (54936, "GB18030"),
    ];

    static EncodingHealthEngine()
    {
        // Legacy codepages aren't registered by default on .NET Core; register once.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Map a detected codepage display name back to its numeric code page (for the fixer).</summary>
    public static int? CodePageFor(string name)
    {
        foreach (var (codePage, candidateName) in Candidates)
        {
            if (string.Equals(candidateName, name, StringComparison.OrdinalIgnoreCase))
                return codePage;
        }
        return null;
    }

    public static EncodingHealthResult Detect(IReadOnlyList<ZipEntryFacts> entries)
    {
        Guard.NotNull(entries);

        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        var broken = 0;

        foreach (var e in entries)
        {
            if (e.IsDirectory)
                continue;

            var raw = e.RawNameBytes;
            if (e.NameIsUtf8 || IsPureAscii(raw) || IsValidUtf8(raw))
                continue; // already correct — not a vote, not broken

            var detected = DetectCodepage(raw);
            if (detected is null)
                broken++;
            else
                votes[detected] = votes.GetValueOrDefault(detected) + 1;
        }

        if (votes.Count == 0 && broken == 0)
            return new EncodingHealthResult(EncodingHealth.Ok, null, 0);

        var winner = ArgMax(votes);

        if (broken > 0 && votes.Count > 0)
            return new EncodingHealthResult(EncodingHealth.PartiallyBroken, winner, broken);
        if (broken > 0)
            return new EncodingHealthResult(EncodingHealth.NeedsFix, null, broken);
        return new EncodingHealthResult(EncodingHealth.NeedsFix, winner, 0);
    }

    /// <summary>
    /// Count entries whose names are legacy-encoded — non-ASCII, no UTF-8 flag, and not valid UTF-8 — i.e. the ones a
    /// Unicode fix would rewrite (whether or not their codepage is detectable). This is the "N entry GBK" figure the
    /// import UI shows; it differs from <see cref="EncodingHealthResult.BrokenEntryCount"/>, which counts only the
    /// <em>undetectable</em> ones. (doc 31 Phase 4.)
    /// </summary>
    public static int CountLegacyEntries(IReadOnlyList<ZipEntryFacts> entries)
    {
        Guard.NotNull(entries);
        var n = 0;
        foreach (var e in entries)
        {
            if (e.IsDirectory)
                continue;
            var raw = e.RawNameBytes;
            if (e.NameIsUtf8 || IsPureAscii(raw) || IsValidUtf8(raw))
                continue;
            n++;
        }
        return n;
    }

    /// <summary>The first candidate codepage that decodes the raw bytes losslessly and cleanly; else null.</summary>
    public static string? DetectCodepage(byte[] raw)
    {
        Guard.NotNull(raw);
        foreach (var (codePage, name) in Candidates)
        {
            if (RoundTripsCleanly(raw, codePage))
                return name;
        }
        return null;
    }

    private static bool RoundTripsCleanly(byte[] raw, int codePage)
    {
        try
        {
            var enc = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var decoded = enc.GetString(raw);      // throws if bytes aren't valid in this codepage
            if (HasControlChars(decoded))
                return false;
            var reencoded = enc.GetBytes(decoded); // throws if not representable
            return reencoded.AsSpan().SequenceEqual(raw);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidUtf8(byte[] raw)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var s = utf8.GetString(raw);
            return !HasControlChars(s);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsPureAscii(byte[] raw)
    {
        foreach (var b in raw)
        {
            if (b >= 0x80)
                return false;
        }
        return true;
    }

    private static bool HasControlChars(string s)
    {
        foreach (var ch in s)
        {
            if (ch == '�')
                return true;
            if (char.IsControl(ch) && ch is not ('\t' or '\n' or '\r'))
                return true;
        }
        return false;
    }

    private static string? ArgMax(Dictionary<string, int> votes)
    {
        string? winner = null;
        var best = 0;
        foreach (var (name, count) in votes)
        {
            if (count > best)
            {
                best = count;
                winner = name;
            }
        }
        return winner;
    }
}
