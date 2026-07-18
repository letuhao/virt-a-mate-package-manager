using VarVault.Common;

namespace VarVault.Domain.Dedup;

/// <summary>Identity + physical facts of a var copy needed to reason about safe deletion.</summary>
public sealed record VarFileDedupFacts(
    long Id,
    string IdentityKey,
    string? ContentSignature,
    string? ContentHash,
    bool IsOnline);

/// <summary>Why a deletion is (not) allowed.</summary>
public enum DeletionBlockReason
{
    None = 0,
    HashNotVerified = 1,
    NoVerifiedOnlineDuplicate = 2,
    DifferentIdentity = 3,
}

/// <summary>Verdict from the one deletion predicate that gates every delete path.</summary>
public sealed record DeletionVerdict(bool CanDelete, DeletionBlockReason Reason)
{
    public static readonly DeletionVerdict Allowed = new(true, DeletionBlockReason.None);
    public static DeletionVerdict Blocked(DeletionBlockReason reason) => new(false, reason);
}

/// <summary>
/// 🔒⚠ The single predicate that gates ALL deletion. A VarFile may be deleted only if another copy of
/// the <b>same IdentityKey</b> is <b>online</b> AND its full <c>ContentHash</c> is computed and
/// <b>equal</b>, AND the candidate is not the last online copy. Cross-identity content matches are never
/// delete candidates (report-only). Heuristic signatures never authorize deletion — only a verified
/// full hash does. (Data-arch §5.5; checklist 4.1/4.2/4.3.)
/// </summary>
public static class DeletionPredicate
{
    /// <summary>
    /// Evaluate whether <paramref name="candidate"/> can be safely deleted, given all known copies of
    /// its identity (the candidate may or may not be included in <paramref name="identityGroup"/>).
    /// </summary>
    public static DeletionVerdict Evaluate(VarFileDedupFacts candidate, IReadOnlyList<VarFileDedupFacts> identityGroup)
    {
        Guard.NotNull(candidate);
        Guard.NotNull(identityGroup);

        // The candidate's own hash must be computed — never delete on a heuristic. (§5.5)
        if (string.IsNullOrEmpty(candidate.ContentHash))
            return DeletionVerdict.Blocked(DeletionBlockReason.HashNotVerified);

        foreach (var other in identityGroup)
        {
            if (other.Id == candidate.Id)
                continue;

            // Cross-identity copies never count as a safety net. (4.2)
            if (!string.Equals(other.IdentityKey, candidate.IdentityKey, StringComparison.Ordinal))
                continue;

            // A verified, online, identical copy makes the candidate redundant and keeps ≥1 online copy.
            if (other.IsOnline &&
                !string.IsNullOrEmpty(other.ContentHash) &&
                string.Equals(other.ContentHash, candidate.ContentHash, StringComparison.Ordinal))
            {
                return DeletionVerdict.Allowed;
            }
        }

        return DeletionVerdict.Blocked(DeletionBlockReason.NoVerifiedOnlineDuplicate);
    }
}
