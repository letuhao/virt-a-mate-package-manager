using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Identity;
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
            .Select(g => new { Ref = g.Key, Count = g.Select(x => x.PackageId).Distinct().Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // doc 26 · F-9 — resolve each missing ref to the owned var a global alias maps it to (fold-key match).
        var foldByRef = rows.ToDictionary(r => r.Ref, r => IdentityFold.Compute(r.Ref));
        var foldKeys = foldByRef.Values.Distinct().ToList();
        var aliasByFold = (await db.VarAliases.AsNoTracking()
                .Where(a => foldKeys.Contains(a.MissingRefKey))
                .Join(db.Packages, a => a.ResolvedPackageId, p => p.Id, (a, p) => new { a.MissingRefKey, p.VarName })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.MissingRefKey)
            .ToDictionary(g => g.Key, g => g.First().VarName);

        return rows
            .Select(r => new MissingDependency(r.Ref, r.Count, aliasByFold.GetValueOrDefault(foldByRef[r.Ref])))
            .OrderByDescending(m => m.NeededByCount).ThenBy(m => m.Ref, StringComparer.Ordinal).ToList();
    }
}
