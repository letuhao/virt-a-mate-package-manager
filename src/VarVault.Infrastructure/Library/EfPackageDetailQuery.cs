using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N9 · Package detail query. Joins Package + read model, the canonical var's content items, all
/// copies (tier/path/lineage), and the forward dependency closure via <see cref="IDependencyGraph"/>.
/// (16-checklist BE-N9.)
/// </summary>
public sealed class EfPackageDetailQuery(VarVaultDbContext db, IDependencyGraph graph) : IPackageDetailQuery
{
    public async Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
            return null;
        var item = await db.PackageListItems.AsNoTracking().FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken).ConfigureAwait(false);
        return new PackageDetailOverview(
            package.Id,
            package.VarName,
            package.IdentityKey,
            package.LicenseType,
            item?.TotalSize ?? 0,
            (item?.Class ?? Domain.Entities.ContentClass.Cold).ToString(),
            package.ReverseDependentCount);
    }

    public async Task<PageResult<long>> GetForwardClosurePageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var forward = await graph.ForwardClosureAsync(packageId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var ordered = forward.OrderBy(x => x).ToList();
        return new PageResult<long>(ordered.Skip(page.Skip).Take(page.SafePageSize).ToList(), ordered.Count, page.SafePageNumber, page.SafePageSize);
    }

    public async Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var canonical = await db.Packages.AsNoTracking()
            .Where(p => p.Id == packageId)
            .Select(p => p.CanonicalVarFileId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (canonical is null)
            return PageResult<ContentItemDto>.Empty(page);

        var query = db.ContentItems.AsNoTracking().Where(c => c.VarFileId == canonical);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(c => c.EntryPath)
            .ThenBy(c => c.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(c => new ContentItemDto(c.Type.ToString(), c.EntryPath, c.IsPreset))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
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
            .ToListAsync(cancellationToken).ConfigureAwait(false);
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
        var forward = await graph.ForwardClosureAsync(packageId, cancellationToken: cancellationToken).ConfigureAwait(false);
        var content = await GetContentItemsPageAsync(packageId, new PageRequest(1, 1000), cancellationToken).ConfigureAwait(false);
        var copies = await GetCopiesPageAsync(packageId, new PageRequest(1, 1000), cancellationToken).ConfigureAwait(false);

        return new PackageDetail(
            PackageId: overview.PackageId,
            VarName: overview.VarName,
            IdentityKey: overview.IdentityKey,
            License: overview.License,
            TotalSize: overview.TotalSize,
            StorageClass: overview.StorageClass,
            DependedOnByCount: overview.DependedOnByCount,
            ForwardClosure: forward,
            ContentItems: content.Items,
            Copies: copies.Items);
    }
}
