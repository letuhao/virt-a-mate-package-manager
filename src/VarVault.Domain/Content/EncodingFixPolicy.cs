using VarVault.Domain.Entities;

namespace VarVault.Domain.Content;

/// <summary>What to do with a var in an automated/batch encoding-fix pass.</summary>
public enum FixDisposition
{
    /// <summary>Healthy — nothing to do.</summary>
    None = 0,
    /// <summary>Confident detection → safe to auto-fix.</summary>
    AutoFix = 1,
    /// <summary>Uncertain → flag for human review; never auto-fix or delete unattended.</summary>
    FlagForReview = 2,
}

/// <summary>
/// ⚠ Decides how a batch/auto encoding-fix treats a var: only a confident, fully-detected, single-
/// codepage break auto-fixes; anything partial or undetectable is flagged for review — originals are
/// never deleted unattended. (Checklist 4.15.)
/// </summary>
public static class EncodingFixPolicy
{
    /// <summary>High confidence = NeedsFix, a codepage was detected, and no entry was undetectable.</summary>
    public static bool IsHighConfidence(EncodingHealthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Health == EncodingHealth.NeedsFix
            && result.DetectedCodepage is not null
            && result.BrokenEntryCount == 0;
    }

    public static FixDisposition Decide(EncodingHealthResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Health switch
        {
            EncodingHealth.Ok or EncodingHealth.Fixed => FixDisposition.None,
            _ when IsHighConfidence(result) => FixDisposition.AutoFix,
            _ => FixDisposition.FlagForReview, // PartiallyBroken / no codepage / broken entries
        };
    }
}
