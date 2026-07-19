using Microsoft.EntityFrameworkCore;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// EF query: unresolved dependency refs grouped by the raw ref, with a needed-by count = distinct
/// source packages that require each. Ordered by most-needed first. (Checklist 2.14.)
/// </summary>
public sealed class EfMissingDepsQuery(VarVaultDbContext db) : IMissingDepsQuery
{
    public async Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Dependencies.AsNoTracking()
            .Where(d => d.IsMissing)
            .Join(db.VarFiles, d => d.VarFileId, v => v.Id, (d, v) => new { d.DependsOnRefRaw, v.PackageId })
            .Where(x => x.PackageId != null)
            .GroupBy(x => x.DependsOnRefRaw)
            .Select(g => new MissingDependency(g.Key, g.Select(x => x.PackageId).Distinct().Count()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderByDescending(m => m.NeededByCount).ThenBy(m => m.Ref, StringComparer.Ordinal).ToList();
    }
}
