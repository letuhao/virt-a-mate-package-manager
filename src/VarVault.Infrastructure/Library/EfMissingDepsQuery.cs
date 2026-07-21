using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// EF query: unresolved dependency refs grouped by the raw ref, with a needed-by count = distinct
/// source packages that require each. Ordered by most-needed first. (Checklist 2.14.)
/// </summary>
public sealed class EfMissingDepsQuery(VarVaultDbContext db) : IMissingDepsQuery
{
    public async Task<PageResult<MissingDependency>> GetPageAsync(
        PageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var grouped = db.Dependencies.AsNoTracking()
            .Where(d => d.IsMissing)
            .Join(db.VarFiles, d => d.VarFileId, v => v.Id, (d, v) => new { d.DependsOnRefRaw, v.PackageId })
            .Where(x => x.PackageId != null)
            .GroupBy(x => x.DependsOnRefRaw)
            .Select(g => new { Ref = g.Key, Count = g.Select(x => x.PackageId).Distinct().Count() });

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var text = searchText.Trim();
            grouped = grouped.Where(x => EF.Functions.Like(x.Ref, $"%{text}%"));
        }

        var total = await grouped.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await grouped
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Ref)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var foldByRef = rows.ToDictionary(r => r.Ref, r => IdentityFold.Compute(r.Ref));
        var foldKeys = foldByRef.Values.Distinct().ToList();
        var aliasByFold = (await db.VarAliases.AsNoTracking()
                .Where(a => foldKeys.Contains(a.MissingRefKey))
                .Join(db.Packages, a => a.ResolvedPackageId, p => p.Id, (a, p) => new { a.MissingRefKey, p.VarName })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.MissingRefKey)
            .ToDictionary(g => g.Key, g => g.First().VarName);

        return new PageResult<MissingDependency>(
            rows.Select(r => new MissingDependency(r.Ref, r.Count, aliasByFold.GetValueOrDefault(foldByRef[r.Ref]))).ToList(),
            total,
            page.SafePageNumber,
            page.SafePageSize);
    }

    public async Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default)
    {
        var first = await GetPageAsync(new PageRequest(1, 100), cancellationToken: cancellationToken).ConfigureAwait(false);
        return first.Items;
    }
}
