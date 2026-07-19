namespace VarVault.Domain.Dependencies;

/// <summary>
/// Transitive dependency queries over the resolved graph. Forward closure powers the "activating this
/// preset will pull in N packages" preview and safe-delete reasoning. Implemented by Infrastructure
/// with a recursive CTE (cycle-safe via set dedup + a depth cap). (Checklist 2.10; 3.8.)
/// </summary>
public interface IDependencyGraph
{
    /// <summary>
    /// The set of packages <paramref name="packageId"/> transitively depends on (via its canonical
    /// var's resolved edges), excluding itself. Terminates on cycles and stops at <paramref name="maxDepth"/>.
    /// </summary>
    Task<IReadOnlyList<long>> ForwardClosureAsync(long packageId, int maxDepth = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// The current latest package of a <c>(Creator, PackageName)</c> family (highest VersionSort), or
    /// null. Index-backed for an effectively-O(1) <c>latest</c> deref. (Checklist 2.6.)
    /// </summary>
    Task<long?> CurrentLatestAsync(string creator, string packageName, CancellationToken cancellationToken = default);
}
