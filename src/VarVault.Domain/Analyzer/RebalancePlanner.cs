using VarVault.Common;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// When a new drive is added at a tier, computes which copies would move to it — those whose class
/// desires the new tier but sit on a different one (online, multi-copy only). (Checklist BE-R6.)
/// </summary>
public static class RebalancePlanner
{
    public static IReadOnlyList<long> CandidatesForNewTier(IEnumerable<MigrationCandidate> candidates, int newTier)
    {
        Guard.NotNull(candidates);
        return candidates
            .Where(c => c.IsOnline
                     && !c.IsSingleCopy
                     && PlacementPolicy.DesiredTier(c.StorageClass) == newTier
                     && c.CurrentTier != newTier)
            .Select(c => c.VarFileId)
            .ToList();
    }
}
