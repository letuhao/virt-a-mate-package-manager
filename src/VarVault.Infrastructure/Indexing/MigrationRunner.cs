using System.IO;
using Microsoft.EntityFrameworkCore;
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
public sealed class MigrationRunner(VarVaultDbContext db, IDurableFileMover mover, ITrashService trash)
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

        // Delete the source last — to trash, never hard-delete (X.1).
        job.State = MigrationState.Deleting;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (File.Exists(sourcePath))
            await trash.TrashAsync(sourcePath, "migration", cancellationToken).ConfigureAwait(false);
        db.VarFiles.Remove(source);

        job.State = MigrationState.Done;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MigrationState.Done;
    }

    private async Task<MigrationState> FailAsync(MigrationJob job, string error, CancellationToken cancellationToken)
    {
        job.State = MigrationState.Failed;
        job.Error = error;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return MigrationState.Failed;
    }
}
