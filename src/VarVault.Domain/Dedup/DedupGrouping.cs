using VarVault.Common;

namespace VarVault.Domain.Dedup;

/// <summary>Logical duplicates within one identity (same IdentityKey + ContentSignature). Candidates for reclaim.</summary>
public sealed record DedupGroup(string IdentityKey, string ContentSignature, IReadOnlyList<VarFileDedupFacts> Members)
{
    /// <summary>⚠ A group with any offline member is unsafe for automated dedup deletion. (4.4)</summary>
    public bool AllOnline => Members.All(m => m.IsOnline);
}

/// <summary>
/// Same content across <b>different</b> identities (e.g. two creators shipping the same asset). Reported
/// for awareness only — never a delete candidate. (4.2)
/// </summary>
public sealed record CrossIdentityMatch(string ContentSignature, IReadOnlyList<VarFileDedupFacts> Members);

/// <summary>The outcome of analyzing a set of var copies for duplication.</summary>
public sealed record DedupAnalysis(
    IReadOnlyList<DedupGroup> WithinIdentity,
    IReadOnlyList<CrossIdentityMatch> CrossIdentity);

/// <summary>
/// Groups var copies for dedup: <b>logical duplicates strictly within one IdentityKey</b> (by
/// ContentSignature), and — separately, report-only — content that matches across identities. Both use
/// the structural <c>ContentSignature</c>, so copies packed into different-sized zips still group (the
/// case whole-file checksums miss). (Data-arch §5.2; checklist 4.5, BE-F4.)
/// </summary>
public static class DedupGrouping
{
    public static DedupAnalysis Analyze(IEnumerable<VarFileDedupFacts> facts)
    {
        Guard.NotNull(facts);

        var withSignature = facts.Where(f => !string.IsNullOrEmpty(f.ContentSignature)).ToList();

        var withinIdentity = new List<DedupGroup>();
        var crossIdentity = new List<CrossIdentityMatch>();

        foreach (var bySignature in withSignature.GroupBy(f => f.ContentSignature!, StringComparer.Ordinal))
        {
            var members = bySignature.ToList();

            // Within-identity duplicate groups (the only delete candidates).
            foreach (var byIdentity in members.GroupBy(f => f.IdentityKey, StringComparer.Ordinal))
            {
                var group = byIdentity.ToList();
                if (group.Count > 1)
                    withinIdentity.Add(new DedupGroup(byIdentity.Key, bySignature.Key, group));
            }

            // Same content spanning multiple identities → report-only.
            var distinctIdentities = members.Select(m => m.IdentityKey).Distinct(StringComparer.Ordinal).Count();
            if (distinctIdentities > 1)
                crossIdentity.Add(new CrossIdentityMatch(bySignature.Key, members));
        }

        return new DedupAnalysis(withinIdentity, crossIdentity);
    }
}
