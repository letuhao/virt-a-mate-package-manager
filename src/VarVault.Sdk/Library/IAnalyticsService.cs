namespace VarVault.Sdk.Library;

/// <summary>A space breakdown row (by creator / type / tier).</summary>
public sealed record SpaceByGroup(string Group, long TotalBytes, int Count);

/// <summary>
/// Library analytics: where space goes (by creator/type/tier) for the analytics screen. (Checklist 5.16.)
/// </summary>
public interface IAnalyticsService
{
    Task<IReadOnlyList<SpaceByGroup>> SpaceByCreatorAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpaceByGroup>> SpaceByTypeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpaceByGroup>> SpaceByTierAsync(CancellationToken cancellationToken = default);
}
