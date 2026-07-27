using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;

namespace VarVault.Domain.Content;

/// <summary>Kind of defect that prevents VaM <c>FileManager.RegisterPackage</c> from loading a var.</summary>
public enum VamLoadDefectKind
{
    DuplicateEntries = 1,
    ZipOpenFailed = 2,
    CorruptZip = 3,
    MissingMeta = 4,
    EncodingNeedsFix = 5,
}

/// <summary>One VaM-load defect found by inspecting zip entry facts (and optional ZipArchive open).</summary>
public sealed record VamLoadDefect(VamLoadDefectKind Kind, string Detail);

/// <summary>
/// Detects defects that crash VaM package refresh — especially duplicate entry keys after path
/// normalize (<c>ArgumentException: An element with the same key already exists</c>).
/// </summary>
public static class VamLoadDefectDetector
{
    /// <summary>
    /// Normalize like VaM/Windows path collapse: <c>\</c>→<c>/</c>, strip <c>./</c>, case-insensitive.
    /// </summary>
    public static string NormalizeEntryKey(string decodedName)
    {
        Guard.NotNull(decodedName);
        var s = decodedName.Replace('\\', '/').Trim();
        while (s.StartsWith("./", StringComparison.Ordinal))
            s = s[2..];
        if (s.EndsWith('/') && s.Length > 1)
            s = s.TrimEnd('/');
        return s;
    }

    /// <summary>
    /// Inspect central-directory facts (and optional ZipArchive open failure) for VaM-load defects.
    /// </summary>
    public static IReadOnlyList<VamLoadDefect> Detect(
        IReadOnlyList<ZipEntryFacts> entries,
        IntegrityStatus catalogIntegrity = IntegrityStatus.Ok,
        EncodingHealth encodingHealth = EncodingHealth.Ok,
        bool zipOpenFailed = false,
        string? zipOpenError = null)
    {
        Guard.NotNull(entries);
        var defects = new List<VamLoadDefect>();

        if (zipOpenFailed)
        {
            defects.Add(new VamLoadDefect(
                VamLoadDefectKind.ZipOpenFailed,
                string.IsNullOrWhiteSpace(zipOpenError)
                    ? "ZipArchive open failed (often duplicate FullName keys)."
                    : zipOpenError!));
        }

            // Never invent CorruptZip from an empty list when we already know DuplicateEntries from CD.
            if ((catalogIntegrity == IntegrityStatus.CorruptZip || entries.Count == 0)
                && catalogIntegrity is not IntegrityStatus.DuplicateEntries)
            {
                if (defects.All(d => d.Kind != VamLoadDefectKind.CorruptZip))
                    defects.Add(new VamLoadDefect(VamLoadDefectKind.CorruptZip, "Unreadable or empty zip central directory."));
            }

            if (!entries.Any(e => e.IsRootMetaJson)
                && catalogIntegrity is not IntegrityStatus.CorruptZip
                and not IntegrityStatus.DuplicateEntries)
                defects.Add(new VamLoadDefect(VamLoadDefectKind.MissingMeta, "Root meta.json is missing."));

        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.IsDirectory)
                continue;
            var raw = e.DecodedNameBestEffort;
            var key = NormalizeEntryKey(raw);
            if (string.IsNullOrEmpty(key))
                continue;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(raw);
        }

        foreach (var (key, names) in groups)
        {
            if (names.Count < 2)
                continue;
            var distinct = names.Distinct(StringComparer.Ordinal).Take(4).ToList();
            var detail = distinct.Count == names.Count
                ? $"Normalized key '{key}' collides ({names.Count}×): {string.Join(" | ", distinct)}"
                : $"Normalized key '{key}' appears {names.Count}× (raw: {string.Join(" | ", distinct)})";
            defects.Add(new VamLoadDefect(VamLoadDefectKind.DuplicateEntries, detail));
        }

        if (encodingHealth is EncodingHealth.NeedsFix or EncodingHealth.PartiallyBroken)
        {
            defects.Add(new VamLoadDefect(
                VamLoadDefectKind.EncodingNeedsFix,
                $"Encoding health is {encodingHealth}; CJK path decode may also collide after fix."));
        }

        return defects;
    }

    /// <summary>
    /// Groups file entries that collide under <see cref="NormalizeEntryKey"/> for the per-collision picker.
    /// </summary>
    public static IReadOnlyList<EntryCollisionGroup> ListCollisions(IReadOnlyList<ZipEntryFacts> entries)
    {
        Guard.NotNull(entries);
        var groups = new Dictionary<string, List<EntryCollisionMember>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (e.IsDirectory)
                continue;
            var raw = e.DecodedNameBestEffort;
            var key = NormalizeEntryKey(raw);
            if (string.IsNullOrEmpty(key))
                continue;
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(new EntryCollisionMember(raw, e.UncompressedSize, e.Crc32));
        }

        var result = new List<EntryCollisionGroup>();
        foreach (var (key, members) in groups)
        {
            if (members.Count < 2)
                continue;
            var suggested = members[0];
            for (var i = 1; i < members.Count; i++)
            {
                if (members[i].UncompressedSize > suggested.UncompressedSize)
                    suggested = members[i];
            }

            result.Add(new EntryCollisionGroup(key, members, suggested.FullName));
        }

        return result;
    }

    /// <summary>Map the strongest structural defect to a catalog <see cref="IntegrityStatus"/>.</summary>
    public static IntegrityStatus? SuggestedIntegrityStatus(IReadOnlyList<VamLoadDefect> defects)
    {
        Guard.NotNull(defects);
        // DuplicateEntries wins over ZipOpenFailed: open failures are often the symptom of colliding keys.
        if (defects.Any(d => d.Kind == VamLoadDefectKind.DuplicateEntries))
            return IntegrityStatus.DuplicateEntries;
        if (defects.Any(d => d.Kind is VamLoadDefectKind.CorruptZip or VamLoadDefectKind.ZipOpenFailed))
            return IntegrityStatus.CorruptZip;
        if (defects.Any(d => d.Kind == VamLoadDefectKind.MissingMeta))
            return IntegrityStatus.MissingMeta;
        return null;
    }
}
