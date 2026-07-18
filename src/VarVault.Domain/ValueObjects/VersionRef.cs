using System.Globalization;

namespace VarVault.Domain.ValueObjects;

/// <summary>A dependency's version reference: an exact number or <c>latest</c>.</summary>
public abstract record VersionRef
{
    public sealed record Exact(int Version) : VersionRef;
    public sealed record Latest : VersionRef;

    public static VersionRef Parse(string token) =>
        token.Equals("latest", StringComparison.OrdinalIgnoreCase)
            ? new Latest()
            : new Exact(int.Parse(token, CultureInfo.InvariantCulture));
}
