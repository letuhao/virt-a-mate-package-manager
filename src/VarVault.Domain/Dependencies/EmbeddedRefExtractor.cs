using VarVault.Common;

namespace VarVault.Domain.Dependencies;

/// <summary>
/// Extracts VaM package references embedded in scene/preset JSON — the <c>Creator.Package.version</c>
/// token that prefixes a packaged path (e.g. <c>"Creator.Package.2:/Custom/Clothing/x.vam"</c>). A
/// tolerant scan over quoted string values; each candidate is validated with <see cref="DependencyRef"/>.
/// Used for embedded var deps (2.1) and UserSave deps (2.13).
/// </summary>
public static class EmbeddedRefExtractor
{
    /// <summary>Distinct raw refs found in <paramref name="json"/> (order-preserving).</summary>
    public static IReadOnlyList<string> Extract(string json)
    {
        Guard.NotNull(json);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        // JSON string values are delimited by unescaped quotes; splitting on '"' yields them at odd indices.
        var parts = json.Split('"');
        for (var i = 1; i < parts.Length; i += 2)
        {
            var value = parts[i];
            var colon = value.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;

            var candidate = value[..colon];
            if (DependencyRef.Parse(candidate).IsFailure)
                continue;

            if (seen.Add(candidate))
                result.Add(candidate);
        }

        return result;
    }
}
