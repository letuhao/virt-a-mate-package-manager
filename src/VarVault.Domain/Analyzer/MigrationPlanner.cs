using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Analyzer;

/// <summary>A copy considered for migration: where it is, what class it is, and its safety facts.</summary>
public sealed record MigrationCandidate(
    long VarFileId,
    int CurrentTier,
    ContentClass StorageClass,
    bool IsOnline,
    bool IsSingleCopy);

/// <summary>A proposed move (never executed by the planner). (BE-P5.)</summary>
public sealed record MigrationProposal(long VarFileId, int FromTier, int ToTier);

/// <summary>Why a misplaced candidate was excluded from proposals.</summary>
public enum MigrationExcludeReason
{
    Offline = 1,
    SingleCopy = 2,
    UnsafeTarget = 3,
}

/// <summary>The plan: proposals plus the excluded candidates with reasons.</summary>
public sealed record MigrationPlan(
    IReadOnlyList<MigrationProposal> Proposals,
    IReadOnlyList<(long VarFileId, MigrationExcludeReason Reason)> Excluded);

/// <summary>
/// ⚠ Diffs each copy's actual tier against its class's desired tier and <b>proposes</b> moves —
/// never executes them (propose-only, BE-P5). Excludes offline members, single copies, and any move
/// whose target tier is removable/network. Pure. (Checklist 5.6, BE-P2/BE-P5.)
/// </summary>
public static class MigrationPlanner
{
    /// <summary>
    /// Build a plan. <paramref name="targetTierIsSafe"/> answers whether a desired tier has a safe
    /// (fixed, online, non-removable/network) destination available.
    /// </summary>
    public static MigrationPlan Plan(IEnumerable<MigrationCandidate> candidates, Func<int, bool> targetTierIsSafe)
    {
        Guard.NotNull(candidates);
        Guard.NotNull(targetTierIsSafe);

        var proposals = new List<MigrationProposal>();
        var excluded = new List<(long, MigrationExcludeReason)>();

        foreach (var c in candidates)
        {
            var desired = PlacementPolicy.DesiredTier(c.StorageClass);
            if (desired == c.CurrentTier)
                continue; // correctly placed

            if (!c.IsOnline)
            {
                excluded.Add((c.VarFileId, MigrationExcludeReason.Offline));
                continue;
            }
            if (c.IsSingleCopy)
            {
                excluded.Add((c.VarFileId, MigrationExcludeReason.SingleCopy));
                continue;
            }
            if (!targetTierIsSafe(desired))
            {
                excluded.Add((c.VarFileId, MigrationExcludeReason.UnsafeTarget));
                continue;
            }

            proposals.Add(new MigrationProposal(c.VarFileId, c.CurrentTier, desired));
        }

        return new MigrationPlan(proposals, excluded);
    }
}
