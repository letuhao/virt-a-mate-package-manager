using System.Text;
using VarVault.Common;

namespace VarVault.Domain.Presets;

/// <summary>
/// The portable text format for a loading preset / alias list: one <c>Creator.Package.version</c> ref
/// per line, <c>#</c> comments and blank lines ignored. Refs are var-name strings so a preset moves
/// between machines and re-resolves on import. (Checklist 3.10/3.11.)
/// </summary>
public static class PresetTextFormat
{
    public static string Serialize(IEnumerable<string> refs, string? header = null)
    {
        Guard.NotNull(refs);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(header))
            sb.Append("# ").AppendLine(header);
        foreach (var r in refs)
        {
            if (!string.IsNullOrWhiteSpace(r))
                sb.AppendLine(r.Trim());
        }
        return sb.ToString();
    }

    public static IReadOnlyList<string> Parse(string text)
    {
        Guard.NotNull(text);
        var result = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            result.Add(line);
        }
        return result;
    }
}
