using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N9 · Package detail query with SQL-paged dependency edges and per-var content items.
/// </summary>
public sealed class EfPackageDetailQuery(VarVaultDbContext db) : IPackageDetailQuery
{
    public async Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
            return null;
        var item = await db.PackageListItems.AsNoTracking().FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken).ConfigureAwait(false);
        var stat = await db.UsageStats.AsNoTracking().FirstOrDefaultAsync(s => s.PackageId == packageId, cancellationToken).ConfigureAwait(false);
        return new PackageDetailOverview(
            package.Id,
            package.VarName,
            package.IdentityKey,
            package.LicenseType,
            item?.TotalSize ?? 0,
            (item?.Class ?? ContentClass.Cold).ToString(),
            package.ReverseDependentCount,
            stat?.IsPinnedHot ?? false,
            stat?.IsForcedCold ?? false);
    }

    public async Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var canonical = await CanonicalVarFileIdAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (canonical is null)
            return PageResult<DependencyEdgeDto>.Empty(page);

        var query = db.Dependencies.AsNoTracking().Where(d => d.VarFileId == canonical.Value);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderBy(d => d.DependsOnRefRaw)
            .ThenBy(d => d.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PageResult<DependencyEdgeDto>(await MapEdgesAsync(rows, cancellationToken).ConfigureAwait(false), total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var packageExists = await db.Packages.AsNoTracking()
            .AnyAsync(p => p.Id == packageId, cancellationToken)
            .ConfigureAwait(false);
        if (!packageExists)
            return PageResult<ReverseDependentDto>.Empty(page);

        var query =
            from d in db.Dependencies.AsNoTracking()
            join v in db.VarFiles.AsNoTracking() on d.VarFileId equals v.Id
            join p in db.Packages.AsNoTracking() on v.PackageId equals p.Id
            where d.ResolvedPackageId == packageId && p.Id != packageId
            select new { p.Id, p.VarName, v.PackageId };

        var total = await query.Select(x => x.Id).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .GroupBy(x => new { x.Id, x.VarName })
            .Select(g => g.Key)
            .OrderBy(x => x.VarName)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = rows.Select(r => r.Id).ToList();
        var types = await db.PackageListItems.AsNoTracking()
            .Where(i => ids.Contains(i.PackageId))
            .Select(i => new { i.PackageId, Type = i.PrimaryType.ToString() })
            .ToDictionaryAsync(x => x.PackageId, x => x.Type, cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(r => new ReverseDependentDto(r.Id, r.VarName, types.GetValueOrDefault(r.Id, "Unknown"))).ToList();
        return new PageResult<ReverseDependentDto>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var packageExists = await db.Packages.AsNoTracking()
            .AnyAsync(p => p.Id == packageId, cancellationToken)
            .ConfigureAwait(false);
        if (!packageExists)
            return PageResult<DependencyEdgeDto>.Empty(page);

        var query = db.SaveDependencies.AsNoTracking().Where(d => d.ResolvedPackageId == packageId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderBy(d => d.DependsOnRefRaw)
            .ThenBy(d => d.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(d => new DependencyEdgeDto(
            d.DependsOnRefRaw,
            d.DependsOnRefKey,
            d.IsMissing ? DependencyResolutionState.Missing : DependencyResolutionState.Resolved,
            false,
            null)).ToList();
        return new PageResult<DependencyEdgeDto>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
        long packageId,
        long? varFileId,
        PageRequest request,
        CancellationToken cancellationToken = default,
        string? typeFilter = null,
        bool loadableOnly = false)
    {
        var page = request.Normalize();
        var targetVar = varFileId ?? await CanonicalVarFileIdAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (targetVar is null)
            return PageResult<ContentItemDto>.Empty(page);

        var query = db.ContentItems.AsNoTracking().Where(c => c.VarFileId == targetVar.Value);
        if (loadableOnly)
            query = query.Where(c => c.IsPreset || c.Type == Domain.Entities.ContentType.Scene);
        if (!string.IsNullOrWhiteSpace(typeFilter) &&
            Enum.TryParse<Domain.Entities.ContentType>(typeFilter, ignoreCase: true, out var typed) &&
            typed != Domain.Entities.ContentType.Unknown)
        {
            query = query.Where(c => c.Type == typed);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderBy(c => c.EntryPath)
            .ThenBy(c => c.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(c => new { c.Id, c.Type, c.EntryPath, c.IsPreset, c.PreviewThumbRef })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(c => new ContentItemDto(
            c.Id,
            c.Type.ToString(),
            c.EntryPath,
            c.IsPreset,
            Domain.Content.PreviewRules.HasPreview(c.Type),
            !string.IsNullOrWhiteSpace(c.PreviewThumbRef))).ToList();
        return new PageResult<ContentItemDto>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var query = db.VarFiles.AsNoTracking().Where(v => v.PackageId == packageId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(v => v.Repository!.Tier)
            .ThenBy(v => v.RelativePath)
            .ThenBy(v => v.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(v => new
            {
                v.Id, v.SizeBytes, v.FixedFromVarFileId, v.RelativePath,
                Tier = v.Repository!.Tier, v.Repository.IsOnline, v.Repository.MountPath,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PageResult<CopyDto>(
            items.Select(c => new CopyDto(c.Id, c.Tier, Path.Combine(c.MountPath, c.RelativePath), c.SizeBytes, c.IsOnline, c.FixedFromVarFileId)).ToList(),
            total,
            page.SafePageNumber,
            page.SafePageSize);
    }

    public async Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var overview = await GetOverviewAsync(packageId, cancellationToken).ConfigureAwait(false);
        if (overview is null)
            return null;
        var copies = await GetCopiesPageAsync(packageId, new PageRequest(1, 50), cancellationToken).ConfigureAwait(false);
        return new PackageDetail(
            overview.PackageId,
            overview.VarName,
            overview.IdentityKey,
            overview.License,
            overview.TotalSize,
            overview.StorageClass,
            overview.DependedOnByCount,
            copies.Items,
            overview.IsPinnedHot,
            overview.IsForcedCold);
    }

    private async Task<long?> CanonicalVarFileIdAsync(long packageId, CancellationToken cancellationToken) =>
        await db.Packages.AsNoTracking()
            .Where(p => p.Id == packageId)
            .Select(p => p.CanonicalVarFileId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<DependencyEdgeDto>> MapEdgesAsync(IReadOnlyList<Dependency> rows, CancellationToken cancellationToken)
    {
        var resolvedIds = rows.Where(r => r.ResolvedPackageId is not null).Select(r => r.ResolvedPackageId!.Value).Distinct().ToList();
        var cards = await db.PackageListItems.AsNoTracking()
            .Where(i => resolvedIds.Contains(i.PackageId))
            .Select(i => new ResolvedPackageCard(i.PackageId, i.VarName, i.PrimaryType.ToString(), i.Class.ToString()))
            .ToDictionaryAsync(c => c.PackageId, cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(d =>
        {
            var state = d.IsMissing
                ? DependencyResolutionState.Missing
                : d.IsVersionSubstituted
                    ? DependencyResolutionState.Substituted
                    : d.ResolvedVia == ResolvedVia.Alias
                        ? DependencyResolutionState.Alias
                        : DependencyResolutionState.Resolved;
            ResolvedPackageCard? card = d.ResolvedPackageId is { } id && cards.TryGetValue(id, out var c) ? c : null;
            return new DependencyEdgeDto(d.DependsOnRefRaw, d.DependsOnRefKey, state, d.IsVersionSubstituted, card);
        }).ToList();
    }
}
