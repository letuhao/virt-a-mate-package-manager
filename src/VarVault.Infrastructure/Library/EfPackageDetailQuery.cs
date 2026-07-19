using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N9 · Package detail query. Joins Package + read model, the canonical var's content items, all
/// copies (tier/path/lineage), and the forward dependency closure via <see cref="IDependencyGraph"/>.
/// (16-checklist BE-N9.)
/// </summary>
public sealed class EfPackageDetailQuery(VarVaultDbContext db, IDependencyGraph graph) : IPackageDetailQuery
{
    public async Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
        if (package is null)
            return null;
        var item = await db.PackageListItems.FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken).ConfigureAwait(false);

        var content = package.CanonicalVarFileId is { } canonical
            ? await db.ContentItems
                .Where(c => c.VarFileId == canonical)
                .Select(c => new ContentItemDto(c.Type.ToString(), c.EntryPath, c.IsPreset))
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

        var copies = await db.VarFiles
            .Where(v => v.PackageId == packageId)
            .Select(v => new
            {
                v.Id, v.SizeBytes, v.FixedFromVarFileId, v.RelativePath,
                Tier = v.Repository!.Tier, v.Repository.IsOnline, v.Repository.MountPath,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var forward = await graph.ForwardClosureAsync(packageId, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PackageDetail(
            PackageId: package.Id,
            VarName: package.VarName,
            IdentityKey: package.IdentityKey,
            License: package.LicenseType,
            TotalSize: item?.TotalSize ?? 0,
            StorageClass: (item?.Class ?? Domain.Entities.ContentClass.Cold).ToString(),
            DependedOnByCount: package.ReverseDependentCount,
            ForwardClosure: forward,
            ContentItems: content,
            Copies: copies.Select(c => new CopyDto(c.Id, c.Tier, Path.Combine(c.MountPath, c.RelativePath), c.SizeBytes, c.IsOnline, c.FixedFromVarFileId)).ToList());
    }
}
