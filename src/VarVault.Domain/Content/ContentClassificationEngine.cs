using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Content;

/// <summary>One classified content entry inside a var (previewable types; assets excluded).</summary>
public sealed record ClassifiedEntry(string EntryPath, ContentType Type, bool IsPreset);

/// <summary>Result of classifying a var's entry paths in one pass. (IDX-5.)</summary>
public sealed record VarContentClassification(
    IReadOnlyList<ClassifiedEntry> Items,
    IReadOnlyDictionary<ContentType, int> Counts,
    ContentType PrimaryType);

/// <summary>
/// Classifies a var's central-directory entry paths into VaM content types with per-type counts and a
/// deterministic <see cref="ContentType"/> precedence, using a <b>precompiled rule set</b> (path-prefix +
/// extension — no per-entry regex). Reproduces the legacy load-bearing type table
/// (<c>docs/varManager/03</c>): rules are independent, last match wins for an entry's type, and each
/// matching rule increments its own counter. (IDX-5, checklist 1.18/1.19, BE-C1/C2/C3.)
/// </summary>
public static class ContentClassificationEngine
{
    // Precedence for PrimaryType (highest first). (Data-arch §5.8 / IDX-5.)
    private static readonly ContentType[] Precedence =
    [
        ContentType.Scene, ContentType.Look, ContentType.Clothing, ContentType.Hairstyle,
        ContentType.Morph, ContentType.Pose, ContentType.Skin, ContentType.Plugin, ContentType.Asset,
    ];

    private sealed record Rule(
        string[] Prefixes,
        string[] Extensions,
        ContentType Type,
        Func<string, bool> IsPreset,
        string CounterKey);

    private static readonly Func<string, bool> Never = _ => false;
    private static readonly Func<string, bool> Always = _ => true;
    private static Func<string, bool> WhenExt(string ext) => path => path.EndsWith(ext, StringComparison.Ordinal);

    // Ordered rule set — compiled once, reused for every entry of every var.
    private static readonly Rule[] Rules =
    [
        new(["saves/scene/"], [".json"], ContentType.Scene, Never, "scene"),
        new(["saves/person/appearance/"], [".json", ".vac"], ContentType.Look, WhenExt(".json"), "look"),
        new(["custom/atom/person/general/", "custom/atom/person/appearance/"], [".json", ".vap"], ContentType.Look, Always, "look"),
        new(["custom/clothing/"], [".vam", ".vap"], ContentType.Clothing, Never, "clothing"),
        new(["custom/atom/person/clothing/"], [".vam", ".vap"], ContentType.Clothing, WhenExt(".vap"), "clothing"),
        new(["custom/hair/"], [".vam", ".vap"], ContentType.Hairstyle, Never, "hair"),
        new(["custom/atom/person/hair/"], [".vam", ".vap"], ContentType.Hairstyle, WhenExt(".vap"), "hair"),
        new(["custom/scripts/", "custom/atom/person/scripts/"], [".cs"], ContentType.Plugin, Never, "plugin_cs"),
        new(["custom/scripts/", "custom/atom/person/scripts/"], [".cslist"], ContentType.Plugin, Never, "plugin_cslist"),
        new(["custom/assets/"], [".assetbundle"], ContentType.Asset, Never, "asset"),
        new(["custom/atom/person/morphs/"], [".vmi", ".vap"], ContentType.Morph, WhenExt(".vap"), "morph"),
        new(["custom/atom/person/pose/"], [".vap"], ContentType.Pose, Always, "pose"),
        new(["saves/person/pose/"], [".json", ".vac"], ContentType.Pose, WhenExt(".json"), "pose"),
        new(["custom/atom/person/skin/"], [".vap"], ContentType.Skin, Always, "skin"),
    ];

    public static VarContentClassification Classify(IEnumerable<string> entryPaths)
    {
        Guard.NotNull(entryPaths);

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeCounts = new Dictionary<ContentType, int>();
        var items = new List<ClassifiedEntry>();

        foreach (var raw in entryPaths)
        {
            var path = Normalize(raw);
            if (path.Length == 0 || path[^1] == '/')
                continue; // directory entry

            ContentType? assignedType = null;
            var assignedPreset = false;

            foreach (var rule in Rules)
            {
                if (!Matches(path, rule))
                    continue;

                counters[rule.CounterKey] = counters.GetValueOrDefault(rule.CounterKey) + 1;
                // Independent rules; the last matching rule wins the entry's type (legacy semantics).
                assignedType = rule.Type;
                assignedPreset = rule.IsPreset(path);
            }

            if (assignedType is { } type && type != ContentType.Asset)
                items.Add(new ClassifiedEntry(raw, type, assignedPreset));
        }

        // Per-type counts. Plugins: cslist count if any, else cs count (legacy rule).
        AddCount(typeCounts, ContentType.Scene, counters.GetValueOrDefault("scene"));
        AddCount(typeCounts, ContentType.Look, counters.GetValueOrDefault("look"));
        AddCount(typeCounts, ContentType.Clothing, counters.GetValueOrDefault("clothing"));
        AddCount(typeCounts, ContentType.Hairstyle, counters.GetValueOrDefault("hair"));
        AddCount(typeCounts, ContentType.Morph, counters.GetValueOrDefault("morph"));
        AddCount(typeCounts, ContentType.Pose, counters.GetValueOrDefault("pose"));
        AddCount(typeCounts, ContentType.Skin, counters.GetValueOrDefault("skin"));
        AddCount(typeCounts, ContentType.Asset, counters.GetValueOrDefault("asset"));
        var cslist = counters.GetValueOrDefault("plugin_cslist");
        var cs = counters.GetValueOrDefault("plugin_cs");
        AddCount(typeCounts, ContentType.Plugin, cslist > 0 ? cslist : cs);

        return new VarContentClassification(items, typeCounts, ChoosePrimary(typeCounts));
    }

    /// <summary>Deterministic primary type: the highest-precedence type with a non-zero count.</summary>
    public static ContentType ChoosePrimary(IReadOnlyDictionary<ContentType, int> counts)
    {
        foreach (var type in Precedence)
        {
            if (counts.TryGetValue(type, out var c) && c > 0)
                return type;
        }
        return ContentType.Unknown;
    }

    private static bool Matches(string path, Rule rule)
    {
        var prefixOk = false;
        foreach (var prefix in rule.Prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                prefixOk = true;
                break;
            }
        }
        if (!prefixOk)
            return false;

        foreach (var ext in rule.Extensions)
        {
            if (path.EndsWith(ext, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void AddCount(Dictionary<ContentType, int> counts, ContentType type, int value)
    {
        if (value > 0)
            counts[type] = value;
    }

    // Case-insensitive, forward-slash normalized (the rule set is authored lowercase).
    private static string Normalize(string entryPath) =>
        entryPath.Replace('\\', '/').ToLowerInvariant();
}
