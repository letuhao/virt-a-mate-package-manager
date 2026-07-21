using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Syncs <see cref="ProfilePackageLink"/> from physical <see cref="ActivationLink"/> rows and refreshes
/// the denormalized active-profile columns on <see cref="PackageListItem"/>.
/// </summary>
public sealed class EfProfilePackageLinkService(
    VarVaultDbContext db,
    IClock clock,
    IWriteQueue writeQueue) : IProfilePackageLinkService
{
    public Task SyncFromActivationLinksAsync(long profileId, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueAsync(async ct =>
        {
            var desired = await (
                    from l in db.ActivationLinks.AsNoTracking()
                    join v in db.VarFiles.AsNoTracking() on l.VarFileId equals v.Id
                    where l.ProfileId == profileId
                          && v.PackageId != null
                          && (l.LinkKind == LinkKind.Install || l.LinkKind == LinkKind.Alias)
                    select new { PackageId = v.PackageId!.Value, l.Reason })
                .GroupBy(x => x.PackageId)
                .Select(g => new { PackageId = g.Key, Reason = g.First().Reason })
                .ToDictionaryAsync(x => x.PackageId, x => x.Reason, ct)
                .ConfigureAwait(false);

            var existing = await db.ProfilePackageLinks
                .Where(x => x.ProfileId == profileId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var now = clock.UtcNow.UtcDateTime;
            foreach (var (packageId, reason) in desired)
            {
                var row = existing.FirstOrDefault(x => x.PackageId == packageId);
                if (row is null)
                {
                    db.ProfilePackageLinks.Add(new ProfilePackageLink
                    {
                        ProfileId = profileId,
                        PackageId = packageId,
                        InstalledAt = now,
                        Reason = reason,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    row.Reason = reason;
                    row.UpdatedAt = now;
                }
            }

            foreach (var row in existing.Where(x => !desired.ContainsKey(x.PackageId)))
                db.ProfilePackageLinks.Remove(row);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken);

    public Task RefreshActiveProfileReadModelAsync(CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueAsync(async ct =>
        {
            var activeProfile = await db.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsActive, ct)
                .ConfigureAwait(false);

            var links = activeProfile is null
                ? new Dictionary<long, DateTime>()
                : await db.ProfilePackageLinks.AsNoTracking()
                    .Where(x => x.ProfileId == activeProfile.Id)
                    .ToDictionaryAsync(x => x.PackageId, x => x.InstalledAt, ct)
                    .ConfigureAwait(false);

            var activeIds = links.Keys.ToHashSet();
            var previouslyActive = await db.PackageListItems
                .Where(x => x.IsActive)
                .Select(x => x.PackageId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var packageId in previouslyActive.Where(id => !activeIds.Contains(id)))
            {
                var item = await db.PackageListItems.FirstAsync(x => x.PackageId == packageId, ct).ConfigureAwait(false);
                item.IsActive = false;
                item.InstalledAt = null;
            }

            foreach (var (packageId, installedAt) in links)
            {
                var item = await db.PackageListItems.FirstOrDefaultAsync(x => x.PackageId == packageId, ct).ConfigureAwait(false);
                if (item is null)
                    continue;
                item.IsActive = true;
                item.InstalledAt = installedAt;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken);
}
