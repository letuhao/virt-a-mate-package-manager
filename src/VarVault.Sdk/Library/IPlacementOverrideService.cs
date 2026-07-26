namespace VarVault.Sdk.Library;

/// <summary>Manual placement overrides (pin hot / force cold) for a package's UsageStat.</summary>
public interface IPlacementOverrideService
{
    /// <summary>
    /// Set pin/force flags with mutual exclusion (pin clears force and vice versa).
    /// Both false restores automatic scoring. Triggers a targeted usage recompute.
    /// </summary>
    Task SetAsync(long packageId, bool pinHot, bool forceCold, CancellationToken cancellationToken = default);
}
