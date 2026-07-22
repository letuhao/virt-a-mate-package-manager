using System.Text.RegularExpressions;
using VarVault.Common;

namespace VarVault.Domain.Dependencies;

/// <summary>
/// Extracts the <c>Creator.Package.version</c> references a VaM error log complains about (missing addon packages,
/// failed loads, unresolved deps). Replaces the old brittle single-line regex: a tolerant candidate scan finds every
/// identity-shaped token in any phrasing (<c>Missing addon package X</c>, <c>X:</c>, a path <c>X:/Custom/…</c>, a
/// bare <c>X.var</c>), and <see cref="DependencyRef.Parse"/> is the authority that validates each — so <c>.latest</c>,
/// CJK creator names, and odd delimiters all survive, and non-identity noise is dropped. Pure. (QoL log-repair.)
/// </summary>
public static partial class VamLogParser
{
    // Candidate = <token>.<token>.(digits|latest). Tokens = letters (incl. CJK) / digits / _ / - / +
    // (no spaces — otherwise "could not load Creator.Pack.2" swallows the English prefix).
    // The regex only *finds* candidates; DependencyRef.Parse decides what's a real ref.
    [GeneratedRegex(@"[\p{L}\p{N}_\-+]+\.[\p{L}\p{N}_\-+]+\.(?:\d+|latest)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CandidateRegex();

    // VaM's canonical phrasing names the MISSING package right after this phrase; the rest of the line often names
    // the *depender* ("… that package<Depender> depends on") — sometimes glued to "package" with no space, so a
    // naive scan would mis-read it. Creator may contain spaces (e.g. "Kamiyama Prod.…"). (Real-log hardened.)
    [GeneratedRegex(@"Missing addon package\s+([\p{L}\p{N}_\-+ ]+?\.[\p{L}\p{N}_\-+]+\.(?:\d+|latest))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MissingPhraseRegex();

    /// <summary>
    /// Distinct, valid dependency refs the log complains about, in first-seen order. A different version of the same
    /// package is a distinct entry (VaM never compares versions). Lines using VaM's "Missing addon package X …"
    /// phrasing yield only X (never the depender named later on the line); other lines are scanned generically.
    /// Empty/garbage in → empty out (never throws).
    /// </summary>
    public static IReadOnlyList<DependencyRef> ExtractRefs(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refs = new List<DependencyRef>();

        foreach (var line in logText.Split('\n'))
        {
            var phrase = MissingPhraseRegex().Matches(line);
            if (phrase.Count > 0)
            {
                // Authoritative: the captured package is the missing one — ignore the depender tail.
                foreach (Match m in phrase)
                    TryAdd(m.Groups[1].Value, seen, refs);
            }
            else
            {
                // Other phrasings ("X:", "failed to load X", a bare "X.var") → scan the whole line.
                foreach (Match m in CandidateRegex().Matches(line))
                    TryAdd(m.Value, seen, refs);
            }
        }
        return refs;
    }

    private static void TryAdd(string token, HashSet<string> seen, List<DependencyRef> refs)
    {
        token = token.Trim();
        if (token.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
            token = token[..^4];

        var parsed = DependencyRef.Parse(token);
        if (parsed.IsFailure)
            return;

        var dep = parsed.Value;
        var key = dep.VersionKind == VersionSpecKind.Latest
            ? dep.FamilyKey + ".latest"
            : dep.FamilyKey + "." + dep.ExactVersion;
        if (seen.Add(key))
            refs.Add(dep);
    }
}
