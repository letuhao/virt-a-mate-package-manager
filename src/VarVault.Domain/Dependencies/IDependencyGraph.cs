namespace VarVault.Domain.Dependencies;

/// <summary>
/// An edge reached during forward closure whose <c>ResolvedPackageId</c> is null — the branch stops here
/// and every descendant is unreachable until the ref is imported or aliased. (Deep dependency closure.)
/// </summary>
public sealed record UnresolvedDependencyEdge(long FromPackageId, string DependsOnRefRaw);

/// <summary>
/// Result of a forward-dependency closure: every transitively reachable package (excluding the root),
/// plus unresolved edges that truncated a branch. (Checklist 2.10; deep dependency closure.)
/// </summary>
public sealed record ForwardClosureResult(
    IReadOnlyList<long> PackageIds,
    IReadOnlyList<UnresolvedDependencyEdge> UnresolvedEdges);

/// <summary>
/// Transitive dependency queries over the resolved graph. Forward closure powers the "activating this
/// preset will pull in N packages" preview and safe-delete reasoning. Implemented by Infrastructure
/// with an iterative breadth-first traversal (visited-set cycle detection; no semantic depth cap).
/// (Checklist 2.10; 3.8.)
/// </summary>
public interface IDependencyGraph
{
    /// <summary>
    /// The set of packages <paramref name="packageId"/> transitively depends on (via its canonical
    /// var's resolved edges), excluding itself. Terminates on cycles via a visited-package set.
    /// </summary>
    Task<IReadOnlyList<long>> ForwardClosureAsync(long packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="ForwardClosureAsync"/>, plus the unresolved dependency refs that stopped a branch.
    /// </summary>
    Task<ForwardClosureResult> ForwardClosureDetailedAsync(long packageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current latest package of a <c>(Creator, PackageName)</c> family (highest VersionSort), or
    /// null. Match uses the fold key so casing/NFC differences cannot miss. (Checklist 2.6.)
    /// </summary>
    Task<long?> CurrentLatestAsync(string creator, string packageName, CancellationToken cancellationToken = default);
}
