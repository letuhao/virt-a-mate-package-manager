using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Entities;
using VarVault.Domain.Migration;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// 🔒⚠ Executes a <see cref="MigrationJob"/> through its durable state machine:
/// Planned/Approved → Copying → Verifying → Renaming → (insert target row, re-point refs) → Deleting
/// (trash source) → Done. Uses the durable mover (copy → flush → verify → atomic rename), re-points
/// every reference to the surviving copy BEFORE trashing the source, and is idempotent on resume (a
/// job already past a step is not redone). (Data-arch §5.6; checklist 5.7/5.12.)
/// </summary>
public sealed class MigrationRunner(
    VarVaultDbContext db,
    IDurableFileMover mover,
    ITrashService trash,
    FreeSpaceLedger ledger)
{
    public async Task<MigrationState> RunAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var job = await db.MigrationJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
            return MigrationState.Failed;
        if (job.State is MigrationState.Done or MigrationState.Failed or MigrationState.Cancelled)
            return job.State;

        var source = await db.VarFiles.FirstOrDefaultAsync(v => v.Id == job.VarFileId, cancellationToken).ConfigureAwait(false);
        if (source is null)
            return await FailAsync(job, "source var no longer exists", cancellationToken).ConfigureAwait(false);

        var sourceRepo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == job.SourceRepositoryId, cancellationToken).ConfigureAwait(false);
        var targetRepo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == job.TargetRepositoryId, cancellationToken).ConfigureAwait(false);
        if (sourceRepo is null || targetRepo is null)
            return await FailAsync(job, "source or target repository missing", cancellationToken).ConfigureAwait(false);

        // ⚠ Never migrate TO a removable/network tier — data could vanish with the drive. (5.11)
        if (targetRepo.MediaType is MediaType.Removable or MediaType.Network)
            return await FailAsync(job, "cannot migrate to a removable/network tier", cancellationToken).ConfigureAwait(false);

        var sizeBytes = Math.Max(1L, source.SizeBytes);
        // Prefer live free space so sequential batch migrates can't reuse a stale register-time FreeBytes.
        var freeBytes = ResolveFreeBytes(targetRepo);
        if (!ledger.TryReserve(targetRepo.Id, sizeBytes, freeBytes, targetRepo.MinFreeBytes))
            return await FailAsync(job, "insufficient free space on target (MinFree reserve)", cancellationToken).ConfigureAwait(false);

        try
        {
            var sourcePath = Path.Combine(sourceRepo.MountPath, source.RelativePath);
            var targetPath = Path.Combine(targetRepo.MountPath, source.RelativePath);

            // Copy → verify → atomic rename (idempotent: skip if the verified target already exists).
            job.State = MigrationState.Copying;
            job.StartedAt ??= DateTime.UtcNow;
            job.TempPath = targetPath + ".partial";
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (!File.Exists(targetPath))
            {
                job.State = MigrationState.Verifying;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var copy = await mover.CopyVerifyRenameAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                if (copy.IsFailure)
                    return await FailAsync(job, copy.Error.Message, cancellationToken).ConfigureAwait(false);
            }

            job.State = MigrationState.Renaming;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Insert the target VarFile row only now that the copy is verified (5.13).
            var targetVar = await db.VarFiles
                .FirstOrDefaultAsync(v => v.RepositoryId == targetRepo.Id && v.RelativePath == source.RelativePath, cancellationToken)
                .ConfigureAwait(false);
            if (targetVar is null)
            {
                targetVar = new VarFile
                {
                    PackageId = source.PackageId,
                    RepositoryId = targetRepo.Id,
                    RelativePath = source.RelativePath,
                    SizeBytes = source.SizeBytes,
                    FileMtime = source.FileMtime,
                    QuickHash = source.QuickHash,
                    ContentSignature = source.ContentSignature,
                    PayloadSignature = source.PayloadSignature,
                    ContentSignatureNoPath = source.ContentSignatureNoPath,
                    ContentHash = source.ContentHash,
                    EncodingHealth = source.EncodingHealth,
                    DetectedCodepage = source.DetectedCodepage,
                    BrokenEntryCount = source.BrokenEntryCount,
                    IntegrityStatus = source.IntegrityStatus,
                    IndexedAt = DateTime.UtcNow,
                };
                db.VarFiles.Add(targetVar);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            // ⚠ Re-point every reference to the surviving copy BEFORE deleting the source (5.12).
            var package = source.PackageId is { } pid
                ? await db.Packages.FirstOrDefaultAsync(p => p.Id == pid, cancellationToken).ConfigureAwait(false)
                : null;
            if (package is not null && package.CanonicalVarFileId == source.Id)
                package.CanonicalVarFileId = targetVar.Id;

            var links = await db.ActivationLinks.Where(l => l.VarFileId == source.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var link in links)
                link.VarFileId = targetVar.Id;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Delete the source last — to trash, never hard-delete (X.1). Raise durability to FULL for the
            // transaction that gates this destructive FS op (5.9), then restore the baseline.
            job.State = MigrationState.Deleting;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var connection = db.Database.GetDbConnection();
            SqlitePragmas.ApplySynchronousFull(connection);
            try
            {
                if (File.Exists(sourcePath))
                    await trash.TrashAsync(sourcePath, "migration", cancellationToken).ConfigureAwait(false);
                db.VarFiles.Remove(source);

                job.State = MigrationState.Done;
                job.CompletedAt = DateTime.UtcNow;
                // Persist capacity so the next sequential migrate sees reduced free (ledger alone
                // only covers concurrent in-flight reservations).
                ApplyCapacityDelta(targetRepo, sourceRepo, sizeBytes);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                SqlitePragmas.RestoreSynchronousNormal(connection);
            }
            return MigrationState.Done;
        }
        finally
        {
            ledger.Release(targetRepo.Id, sizeBytes);
        }
    }

    /// <summary>
    /// Prefer the more conservative of live drive free space and catalog FreeBytes.
    /// Catalog FreeBytes is decremented after each successful move so sequential batches honor MinFree
    /// even when live AvailableFreeSpace barely changes (same-volume moves / coarse OS reporting).
    /// </summary>
    private static long ResolveFreeBytes(Repository targetRepo)
    {
        var live = TryReadLiveFreeBytes(targetRepo.MountPath);
        if (live is { } available && targetRepo.FreeBytes is { } catalog && catalog >= 0)
            return Math.Min(available, catalog);
        if (live is { } availableOnly)
            return availableOnly;
        if (targetRepo.FreeBytes is { } known && known >= 0)
            return known;
        return 0;
    }

    private static long? TryReadLiveFreeBytes(string mountPath)
    {
        try
        {
            var root = Path.GetPathRoot(mountPath);
            if (string.IsNullOrEmpty(root))
                return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Update catalog free estimates after a successful move so the next sequential migrate
    /// sees reduced free (ledger alone only covers concurrent in-flight reservations).
    /// </summary>
    private static void ApplyCapacityDelta(Repository targetRepo, Repository sourceRepo, long sizeBytes)
    {
        var targetLive = TryReadLiveFreeBytes(targetRepo.MountPath);
        var targetCatalog = Math.Max(0, (targetRepo.FreeBytes ?? targetLive ?? 0) - sizeBytes);
        targetRepo.FreeBytes = targetLive is { } tl ? Math.Min(tl, targetCatalog) : targetCatalog;

        var sourceLive = TryReadLiveFreeBytes(sourceRepo.MountPath);
        var sourceCatalog = (sourceRepo.FreeBytes ?? 0) + sizeBytes;
        sourceRepo.FreeBytes = sourceLive is { } sl ? Math.Min(sl, sourceCatalog) : sourceCatalog;
    }

    private async Task<MigrationState> FailAsync(MigrationJob job, string error, CancellationToken cancellationToken)
    {
        job.State = MigrationState.Failed;
        job.Error = error;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MigrationState.Failed;
    }
}
