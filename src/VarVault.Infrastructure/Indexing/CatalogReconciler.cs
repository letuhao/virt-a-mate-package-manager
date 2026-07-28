using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>Summary of a filesystem-truth reconcile.</summary>
public sealed record ReconcileResult(int ReposOnline, int ReposOffline, int VarFilesPruned, int ListRowsHealed = 0);

/// <summary>
/// ⚠ Reconciles the catalog against filesystem truth — repositories are re-marked online/offline by
/// whether their mount exists, and vanished files in <b>online</b> repos are pruned (offline repos are
/// left untouched: offline ≠ gone). Also heals packages that have VarFiles but never got a Library
/// <c>PackageListItem</c> (Exact-visible / Library-invisible gap). Run at app launch and after a DB
/// restore before any pending job/trash action. (Checklist X.5.)
/// </summary>
public sealed class CatalogReconciler(VarVaultDbContext db, ICatalogStore store)
{
    public async Task<ReconcileResult> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var repos = await db.Repositories.ToListAsync(cancellationToken).ConfigureAwait(false);

        var online = 0;
        var offline = 0;
        var onlineRepoIds = new List<(Guid Id, string MountPath)>();
        foreach (var repo in repos)
        {
            var exists = Directory.Exists(repo.MountPath);
            repo.IsOnline = exists;
            if (exists)
            {
                online++;
                onlineRepoIds.Add((repo.Id, repo.MountPath));
            }
            else
            {
                offline++;
            }
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var pruned = 0;
        var affectedPackages = new HashSet<long>();
        foreach (var (repoId, mountPath) in onlineRepoIds)
        {
            var varFiles = await db.VarFiles
                .Where(v => v.RepositoryId == repoId)
                .Select(v => new { v.Id, v.RelativePath, v.PackageId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var gone = varFiles
                .Where(v => !FileExistsOnMount(mountPath, v.RelativePath))
                .ToList();

            if (gone.Count == 0)
                continue;

            foreach (var v in gone)
            {
                if (v.PackageId is long pkg)
                    affectedPackages.Add(pkg);
            }

            pruned += await store.RemoveVarFilesAsync(
                gone.Select(v => v.Id).ToList(), cancellationToken).ConfigureAwait(false);
        }

        // Heal Exact-visible / Library-invisible packages: Package+VarFile exist, PackageListItem does not.
        var orphanListIds = await db.Packages.AsNoTracking()
            .Where(p => p.VarFiles.Any() && !db.PackageListItems.Any(i => i.PackageId == p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var id in orphanListIds)
            affectedPackages.Add(id);

        var healed = orphanListIds.Count;
        if (affectedPackages.Count > 0)
            await store.RefreshReadModelAsync(affectedPackages.ToList(), cancellationToken).ConfigureAwait(false);

        return new ReconcileResult(online, offline, pruned, healed);
    }

    /// <summary>Resolve RelativePath under the mount the same way the enumerator stores it.</summary>
    private static bool FileExistsOnMount(string mountPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        try
        {
            var full = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.GetFullPath(Path.Combine(mountPath, relativePath));
            return File.Exists(full);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
