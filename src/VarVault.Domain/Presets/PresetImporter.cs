using VarVault.Common;
using VarVault.Domain.Dependencies;

namespace VarVault.Domain.Presets;

/// <summary>One available version of a package family, for import resolution.</summary>
public sealed record AvailablePackageVersion(long VersionSort, string VarName);

/// <summary>How an imported ref resolved against the local library.</summary>
public enum PresetImportStatus
{
    Found = 0,
    VersionSubstituted = 1,
    Unknown = 2,
    Unparseable = 3,
}

/// <summary>One imported ref plus its resolution against the current library.</summary>
public sealed record PresetImportItem(string Raw, PresetImportStatus Status, string? ResolvedVarName);

/// <summary>The reported diff from importing a preset. (Checklist 3.10/3.11.)</summary>
public sealed record PresetImportResult(IReadOnlyList<PresetImportItem> Items)
{
    public int FoundCount => Items.Count(i => i.Status == PresetImportStatus.Found);
    public int SubstitutedCount => Items.Count(i => i.Status == PresetImportStatus.VersionSubstituted);
    public int UnknownCount => Items.Count(i => i.Status is PresetImportStatus.Unknown or PresetImportStatus.Unparseable);
}

/// <summary>
/// Re-resolves imported var-name refs against the local library and reports a diff (found /
/// version-substituted / unknown / unparseable) — so importing a portable preset on a different
/// machine surfaces exactly what changed. Pure. (Checklist 3.10/3.11.)
/// </summary>
public static class PresetImporter
{
    public static PresetImportResult Analyze(
        IEnumerable<string> refs,
        IReadOnlyDictionary<string, IReadOnlyList<AvailablePackageVersion>> familyMap)
    {
        Guard.NotNull(refs);
        Guard.NotNull(familyMap);

        var items = new List<PresetImportItem>();
        foreach (var raw in refs)
        {
            items.Add(Resolve(raw, familyMap));
        }
        return new PresetImportResult(items);
    }

    private static PresetImportItem Resolve(string raw, IReadOnlyDictionary<string, IReadOnlyList<AvailablePackageVersion>> familyMap)
    {
        var parsed = DependencyRef.Parse(raw);
        if (parsed.IsFailure)
            return new PresetImportItem(raw, PresetImportStatus.Unparseable, null);

        var reference = parsed.Value;
        if (!familyMap.TryGetValue(reference.FamilyKey, out var available) || available.Count == 0)
            return new PresetImportItem(raw, PresetImportStatus.Unknown, null);

        if (reference.VersionKind == VersionSpecKind.Latest)
        {
            var latest = available.MaxBy(v => v.VersionSort)!;
            // A "latest"/"$"-ref is Found (it always means "the newest"); a substitution token notes it.
            var status = reference.IsSubstitutionToken ? PresetImportStatus.VersionSubstituted : PresetImportStatus.Found;
            return new PresetImportItem(raw, status, latest.VarName);
        }

        var exact = available.FirstOrDefault(v => v.VersionSort == reference.ExactVersion);
        if (exact is not null)
            return new PresetImportItem(raw, PresetImportStatus.Found, exact.VarName);

        // Version not present → substitute the closest (newer preferred, else newest older).
        var newer = available.Where(v => v.VersionSort > reference.ExactVersion).OrderBy(v => v.VersionSort).FirstOrDefault();
        var chosen = newer ?? available.OrderByDescending(v => v.VersionSort).First();
        return new PresetImportItem(raw, PresetImportStatus.VersionSubstituted, chosen.VarName);
    }
}
