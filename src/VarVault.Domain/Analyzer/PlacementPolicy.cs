using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// Maps a package's storage class to its desired tier (Hot→1, Warm→2, Cold→3) and decides whether a
/// copy is misplaced (actual tier ≠ desired). The basis for migration proposals. (BE-P1, 5.5.)
/// </summary>
public static class PlacementPolicy
{
    public static int DesiredTier(ContentClass storageClass) => storageClass switch
    {
        ContentClass.Hot => TierPolicy.HotTier,
        ContentClass.Warm => TierPolicy.WarmTier,
        _ => TierPolicy.ColdTier,
    };

    /// <summary>True if a copy on <paramref name="actualTier"/> should move to match its class.</summary>
    public static bool IsMisplaced(ContentClass storageClass, int actualTier) =>
        actualTier != DesiredTier(storageClass);
}
