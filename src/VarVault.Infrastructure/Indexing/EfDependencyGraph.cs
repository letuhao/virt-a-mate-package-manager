using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Forward dependency closure via iterative breadth-first traversal over resolved edges of each
/// package's canonical var. A visited-package set makes it cycle-safe; there is no semantic depth
/// cap — the finite catalog is the natural bound. Unresolved edges are reported rather than silently
/// truncating descendant discovery. (Checklist 2.10; deep dependency closure.)
/// </summary>
public sealed class EfDependencyGraph(VarVaultDbContext db) : IDependencyGraph
{
    /// <summary>SQLite parameter budget is limited — chunk frontier IN-lists.</summary>
    private const int FrontierChunkSize = 500;

    public async Task<IReadOnlyList<long>> ForwardClosureAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var detailed = await ForwardClosureDetailedAsync(packageId, cancellationToken).ConfigureAwait(false);
        return detailed.PackageIds;
    }

    public async Task<ForwardClosureResult> ForwardClosureDetailedAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<long> { packageId };
        var reachable = new List<long>();
        var unresolved = new List<UnresolvedDependencyEdge>();
        var frontier = new List<long> { packageId };

        while (frontier.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Packages with no canonical var cannot contribute edges — report so the branch is visible.
            for (var offset = 0; offset < frontier.Count; offset += FrontierChunkSize)
            {
                var chunk = frontier.Skip(offset).Take(FrontierChunkSize).ToList();
                var noCanonical = await db.Packages.AsNoTracking()
                    .Where(p => chunk.Contains(p.Id) && p.CanonicalVarFileId == null)
                    .Select(p => new { p.Id, p.VarName })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (var pkg in noCanonical)
                    unresolved.Add(new UnresolvedDependencyEdge(pkg.Id, $"canonical-missing:{pkg.VarName}"));
            }

            var edges = new List<(long FromPackageId, long? ResolvedPackageId, string DependsOnRefRaw)>();
            for (var offset = 0; offset < frontier.Count; offset += FrontierChunkSize)
            {
                var chunk = frontier.Skip(offset).Take(FrontierChunkSize).ToList();
                var page = await (
                    from p in db.Packages.AsNoTracking()
                    where chunk.Contains(p.Id) && p.CanonicalVarFileId != null
                    join d in db.Dependencies.AsNoTracking() on p.CanonicalVarFileId equals d.VarFileId
                    select new
                    {
                        FromPackageId = p.Id,
                        d.ResolvedPackageId,
                        d.DependsOnRefRaw,
                    }).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var e in page)
                    edges.Add((e.FromPackageId, e.ResolvedPackageId, e.DependsOnRefRaw));
            }

            var next = new List<long>();
            foreach (var edge in edges)
            {
                if (edge.ResolvedPackageId is { } target)
                {
                    if (visited.Add(target))
                    {
                        reachable.Add(target);
                        next.Add(target);
                    }
                }
                else
                {
                    unresolved.Add(new UnresolvedDependencyEdge(edge.FromPackageId, edge.DependsOnRefRaw));
                }
            }

            frontier = next;
        }

        return new ForwardClosureResult(reachable, unresolved);
    }

    public async Task<long?> CurrentLatestAsync(string creator, string packageName, CancellationToken cancellationToken = default)
    {
        // Fold-compare family identity — raw Creator/PackageName equality misses casing/NFC variants.
        var familyKey = IdentityFold.Compute($"{creator}.{packageName}");
        var candidates = await db.Packages.AsNoTracking()
            .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Where(p => IdentityFold.Compute($"{p.Creator}.{p.PackageName}") == familyKey)
            .OrderByDescending(p => p.VersionSort)
            .Select(p => (long?)p.Id)
            .FirstOrDefault();
    }
}
