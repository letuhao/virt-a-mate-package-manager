namespace VarVault.Domain.Dependencies;

/// <summary>Summary of a catalog-wide dependency resolution pass.</summary>
public sealed record DependencyResolutionResult(int Resolved, int Missing, int Foundational);

/// <summary>
/// Resolves every harvested dependency edge against the catalog in one pass: sets
/// <c>ResolvedPackageId</c>/<c>IsMissing</c>/<c>IsVersionSubstituted</c>/<c>ResolvedVia</c> (real match
/// outranks alias; SELF refs resolve to the container and are never missing), then maintains
/// <c>ReverseDependentCount</c>/<c>IsFoundational</c> and the materialized <c>HasMissingDeps</c> bit.
/// Implemented by Infrastructure over EF. (Checklist 2.2/2.5/2.8/2.9/2.11/2.15.)
/// </summary>
public interface IDependencyResolver
{
    Task<DependencyResolutionResult> ResolveAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Incrementally re-resolve only the edges targeting one <c>(Creator, PackageName)</c> family —
    /// e.g. after a new version arrives, so <c>latest</c> refs update without touching unrelated edges.
    /// Returns the number of edges re-resolved. (Checklist 2.7.)
    /// </summary>
    Task<int> ResolveFamilyAsync(string creator, string packageName, CancellationToken cancellationToken = default);
}
