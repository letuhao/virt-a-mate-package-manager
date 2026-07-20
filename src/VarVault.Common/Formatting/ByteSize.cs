using System.Globalization;

namespace VarVault.Common.Formatting;

/// <summary>
/// Single source of truth for humanizing a byte count into a compact, culture-invariant size string
/// (e.g. <c>1.55 TB</c>, <c>487.7 GB</c>, <c>812 MB</c>, <c>64 KB</c>, <c>0 B</c>). Replaces the ad-hoc
/// <c>bytes / (1&lt;&lt;30)</c> helpers that were copy-pasted across view-models — and the raw-byte bindings
/// that showed unreadable numbers on the Dashboard / Duplicates / Library grid. (28-checklist A1.)
/// </summary>
public static class ByteSize
{
    private const long Kib = 1L << 10;
    private const long Mib = 1L << 20;
    private const long Gib = 1L << 30;
    private const long Tib = 1L << 40;

    /// <summary>Format <paramref name="bytes"/> with the largest fitting unit; negatives clamp to <c>0 B</c>.</summary>
    public static string Humanize(long bytes)
    {
        if (bytes <= 0)
            return "0 B";
        return bytes switch
        {
            >= Tib => Format(bytes / (double)Tib, "TB"),
            >= Gib => Format(bytes / (double)Gib, "GB"),
            >= Mib => Format(bytes / (double)Mib, "MB"),
            >= Kib => Format(bytes / (double)Kib, "KB"),
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        };
    }

    // Precision scales with the unit: TB two decimals ("1.55 TB"), GB one ("487.7 GB"), KB/MB none ("812 MB") —
    // decimals on small units read as noise, but multi-TB drives need them to stay comparable.
    private static string Format(double value, string unit) => unit switch
    {
        "TB" => string.Create(CultureInfo.InvariantCulture, $"{value:F2} {unit}"),
        "GB" => string.Create(CultureInfo.InvariantCulture, $"{value:F1} {unit}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{value:F0} {unit}"),
    };
}
