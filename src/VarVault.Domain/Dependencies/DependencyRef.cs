using VarVault.Common;
using VarVault.Domain.Identity;

namespace VarVault.Domain.Dependencies;

/// <summary>How a dependency reference names its version.</summary>
public enum VersionSpecKind
{
    Exact = 0,
    Latest = 1,
}

/// <summary>
/// A parsed dependency reference <c>Creator.Package.version</c> (version = digits or <c>latest</c>).
/// <see cref="FamilyKey"/> folds <c>Creator.Package</c> for matching a package across versions;
/// refs containing a <c>$</c> substitution token or that don't parse are flagged. (IDX-4, 2.2/2.5.)
/// </summary>
public sealed record DependencyRef(
    string Creator,
    string Package,
    VersionSpecKind VersionKind,
    long ExactVersion,
    bool IsSelf,
    bool IsSubstitutionToken)
{
    /// <summary>Folded <c>Creator.Package</c> key — groups all versions of one package for resolution.</summary>
    public string FamilyKey => IdentityFold.Compute($"{Creator}.{Package}");

    /// <summary>Parse a raw dependency ref. <paramref name="containerFamilyKey"/> marks SELF refs.</summary>
    public static Result<DependencyRef> Parse(string raw, string? containerFamilyKey = null)
    {
        Guard.NotNull(raw);
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return Result.Failure<DependencyRef>("dep.empty", "empty dependency ref");

        // A "$" token (e.g. version macro) can't be resolved deterministically — flag, never drop.
        var hasSubstitution = trimmed.Contains('$', StringComparison.Ordinal);

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
            return Result.Failure<DependencyRef>("dep.format", $"'{raw}' is not Creator.Package.Version");

        var creator = parts[0];
        var package = parts[1];
        var version = parts[2];

        VersionSpecKind kind;
        long exact = 0;
        if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            kind = VersionSpecKind.Latest;
        }
        else if (version.Length > 0 && version.All(char.IsAsciiDigit))
        {
            kind = VersionSpecKind.Exact;
            exact = ValueObjects.PackageId.ParseVersionSort(version);
        }
        else if (hasSubstitution)
        {
            // e.g. "Creator.Package.$VERSION" — treat as latest but flag as substituted.
            kind = VersionSpecKind.Latest;
        }
        else
        {
            return Result.Failure<DependencyRef>("dep.version", $"version '{version}' is not a number or 'latest'");
        }

        var familyKey = IdentityFold.Compute($"{creator}.{package}");
        var isSelf = containerFamilyKey is not null &&
                     string.Equals(familyKey, containerFamilyKey, StringComparison.Ordinal);

        return new DependencyRef(creator, package, kind, exact, isSelf, hasSubstitution);
    }
}
