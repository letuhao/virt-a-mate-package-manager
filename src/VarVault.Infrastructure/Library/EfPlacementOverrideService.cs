using VarVault.Domain.Analyzer;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>Sdk facade over <see cref="IUsageAnalyzer.SetPlacementOverridesAsync"/>.</summary>
public sealed class EfPlacementOverrideService(IUsageAnalyzer usage) : IPlacementOverrideService
{
    public Task SetAsync(long packageId, bool pinHot, bool forceCold, CancellationToken cancellationToken = default) =>
        usage.SetPlacementOverridesAsync(packageId, pinHot, forceCold, cancellationToken);
}
