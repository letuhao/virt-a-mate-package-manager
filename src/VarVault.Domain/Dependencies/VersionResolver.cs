using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Dependencies;

/// <summary>One available version of a package family (for resolving a dependency ref).</summary>
public sealed record AvailableVersion(long VersionSort, long PackageId);

/// <summary>The chosen package for a dependency ref plus how it was chosen.</summary>
public sealed record VersionResolution(long PackageId, bool IsVersionSubstituted, ResolvedVia Via);

/// <summary>
/// Resolves a dependency ref's version against the available versions of its package family:
/// <c>latest</c> → highest; exact → that version if present, else the <b>closest newer</b>, else the
/// <b>newest older</b> (both flagged substituted). No versions available → unresolved (missing).
/// Pure and deterministic. (Checklist 2.5.)
/// </summary>
public static class VersionResolver
{
    public static VersionResolution? Resolve(DependencyRef reference, IReadOnlyList<AvailableVersion> available)
    {
        Guard.NotNull(reference);
        Guard.NotNull(available);
        if (available.Count == 0)
            return null;

        if (reference.VersionKind == VersionSpecKind.Latest)
        {
            var latest = available.MaxBy(v => v.VersionSort)!;
            // A "$"-substituted ref resolved as latest is a substitution; a plain "latest" is not.
            return new VersionResolution(latest.PackageId, reference.IsSubstitutionToken, ResolvedVia.Latest);
        }

        var exact = available.FirstOrDefault(v => v.VersionSort == reference.ExactVersion);
        if (exact is not null)
            return new VersionResolution(exact.PackageId, IsVersionSubstituted: false, ResolvedVia.Exact);

        // Closest newer, else newest older.
        var newer = available.Where(v => v.VersionSort > reference.ExactVersion).OrderBy(v => v.VersionSort).FirstOrDefault();
        if (newer is not null)
            return new VersionResolution(newer.PackageId, IsVersionSubstituted: true, ResolvedVia.Closest);

        var older = available.Where(v => v.VersionSort < reference.ExactVersion).OrderByDescending(v => v.VersionSort).First();
        return new VersionResolution(older.PackageId, IsVersionSubstituted: true, ResolvedVia.Closest);
    }
}
