using VarVault.Common;
using VarVault.Domain.Identity;

namespace VarVault.Domain.ValueObjects;

/// <summary>
/// The logical identity of a var: <c>Creator.Package.Version</c>, parsed from the
/// verbatim filename (never meta.json). The version token is preserved as-is
/// (so <c>.007</c> stays <c>.007</c>) to keep symlink targets and dependency
/// matching exact.
/// </summary>
public sealed record PackageId
{
    public string Creator { get; }
    public string Package { get; }

    /// <summary>Version token exactly as it appeared in the filename (digits, leading zeros preserved).</summary>
    public string VersionToken { get; }

    public string VarName => $"{Creator}.{Package}.{VersionToken}";

    /// <summary>The fold key (NFC + case-fold) used for all identity matching. See <see cref="IdentityFold"/>.</summary>
    public string IdentityKey => IdentityFold.Compute(VarName);

    /// <summary>
    /// Numeric projection of the version token for sorting. Clamped so an absurdly long version
    /// (11+ digits) can never overflow — the verbatim <see cref="VersionToken"/> stays authoritative.
    /// </summary>
    public long VersionSort => ParseVersionSort(VersionToken);

    /// <summary>Parse a digit version token to a sortable long, clamping instead of overflowing.</summary>
    public static long ParseVersionSort(string versionToken)
    {
        long value = 0;
        foreach (var ch in versionToken)
        {
            if (!char.IsAsciiDigit(ch))
                break;
            // Clamp: once we'd exceed long.MaxValue, stop accumulating and saturate.
            if (value > (long.MaxValue - (ch - '0')) / 10)
                return long.MaxValue;
            value = (value * 10) + (ch - '0');
        }
        return value;
    }

    private PackageId(string creator, string package, string versionToken)
    {
        Creator = creator;
        Package = package;
        VersionToken = versionToken;
    }

    /// <summary>Parse a var name or file name. Rejects non-3-part names and non-numeric versions.</summary>
    public static Result<PackageId> TryParse(string nameOrFile)
    {
        var name = nameOrFile.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
            ? nameOrFile[..^4]
            : nameOrFile;

        var parts = name.Split('.');
        if (parts.Length != 3)
            return Result.Failure<PackageId>("packageid.format", $"'{nameOrFile}' is not Creator.Package.Version");

        var version = parts[2];
        if (version.Length == 0 || !version.All(char.IsAsciiDigit))
            return Result.Failure<PackageId>("packageid.version", $"version '{version}' is not numeric");

        return new PackageId(parts[0], parts[1], version);
    }
}
