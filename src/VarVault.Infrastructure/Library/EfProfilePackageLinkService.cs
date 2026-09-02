using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    IClock clock,
    IWriteQueue writeQueue) : IProfilePackageLinkService
{
    private const int IdChunkSize = 500;

    public Task SyncFromActivationLinksAsync(long profileId, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
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
        writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var activeProfile = await db.Profiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.IsActive, ct)
                .ConfigureAwait(false);

            var links = activeProfile is null
                ? new Dictionary<long, DateTime>()
                : await db.ProfilePackageLinks.AsNoTracking()
                    .Where(x => x.ProfileId == activeProfile.Id)
                    .ToDictionaryAsync(x => x.PackageId, x => x.InstalledAt, ct)
                    .ConfigureAwait(false);

            await db.PackageListItems
                .Where(x => x.IsActive)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.IsActive, false).SetProperty(x => x.InstalledAt, (DateTime?)null),
                    ct)
                .ConfigureAwait(false);

            if (links.Count == 0)
                return;

            var activeIds = links.Keys.ToList();
            var items = new Dictionary<long, PackageListItem>();
            for (var offset = 0; offset < activeIds.Count; offset += IdChunkSize)
            {
                var chunk = activeIds.Skip(offset).Take(IdChunkSize).ToList();
                var page = await db.PackageListItems
                    .Where(x => chunk.Contains(x.PackageId))
                    .ToDictionaryAsync(x => x.PackageId, ct)
                    .ConfigureAwait(false);
                foreach (var (key, value) in page)
                    items[key] = value;
            }

            foreach (var (packageId, installedAt) in links)
            {
                if (!items.TryGetValue(packageId, out var item))
                    continue;
                item.IsActive = true;
                item.InstalledAt = installedAt;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, WritePriority.Interactive, cancellationToken);
}
