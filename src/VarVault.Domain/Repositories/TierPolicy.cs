using VarVault.Domain.Entities;

namespace VarVault.Domain.Repositories;

/// <summary>
/// Assigns a storage tier from a repository's measured speed and media type. Faster = lower tier
/// number (Tier 1 = hot). Speed thresholds win when a benchmark exists; otherwise media type decides.
/// Thresholds are configurable. (Checklist BE-R5, 1.6.)
/// </summary>
public sealed class TierPolicy(
    double tier1MinReadMBps = 3000,
    double tier2MinReadMBps = 800)
{
    public static readonly TierPolicy Default = new();

    public const int HotTier = 1;
    public const int WarmTier = 2;
    public const int ColdTier = 3;

    /// <summary>Tier from a measured read speed (MB/s) when available, else from <see cref="MediaType"/>.</summary>
    public int AssignTier(MediaType mediaType, double? readMBps)
    {
        // Removable/network media is always cold regardless of a fast burst benchmark.
        if (mediaType is MediaType.Removable or MediaType.Network)
            return ColdTier;

        if (readMBps is { } speed && speed > 0)
        {
            if (speed >= tier1MinReadMBps)
                return HotTier;
            if (speed >= tier2MinReadMBps)
                return WarmTier;
            return ColdTier;
        }

        return mediaType switch
        {
            MediaType.Nvme => HotTier,
            MediaType.Ssd => WarmTier,
            _ => ColdTier, // HDD / Unknown → cold (safe default)
        };
    }
}
