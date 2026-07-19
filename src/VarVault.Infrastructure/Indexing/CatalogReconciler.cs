using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>Summary of a filesystem-truth reconcile.</summary>
public sealed record ReconcileResult(int ReposOnline, int ReposOffline, int VarFilesPruned);

/// <summary>
/// ⚠ Reconciles the catalog against filesystem truth — repositories are re-marked online/offline by
/// whether their mount exists, and vanished files in <b>online</b> repos are pruned (offline repos are
/// left untouched: offline ≠ gone). Run after a DB restore before any pending job/trash action, so
/// stale rows can't drive a destructive action. (Checklist X.5.)
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
        foreach (var (repoId, mountPath) in onlineRepoIds)
        {
            var varFiles = await db.VarFiles
                .Where(v => v.RepositoryId == repoId)
                .Select(v => new { v.Id, v.RelativePath })
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            var gone = varFiles
                .Where(v => !File.Exists(Path.Combine(mountPath, v.RelativePath)))
                .Select(v => v.Id)
                .ToList();

            if (gone.Count > 0)
                pruned += await store.RemoveVarFilesAsync(gone, cancellationToken).ConfigureAwait(false);
        }

        return new ReconcileResult(online, offline, pruned);
    }
}
