using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>EF implementation of the durable scan ledger. (A15.)</summary>
public sealed class ScanLedger(VarVaultDbContext db, IClock clock) : IScanLedger
{
    public async Task<ScanRun> BeginRunAsync(Guid? repositoryId, CancellationToken cancellationToken = default)
    {
        var lastGen = await db.ScanRuns
            .Where(r => r.RepositoryId == repositoryId)
            .Select(r => (long?)r.Generation)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        var run = new ScanRun
        {
            PublicId = Guid.NewGuid(),
            RepositoryId = repositoryId,
            Generation = lastGen + 1,
            Phase = ScanPhase.Discovering,
            StartedAt = clock.UtcNow.UtcDateTime,
        };
        db.ScanRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.ChangeTracker.Clear();
        return run;
    }

    public async Task SetPhaseAsync(long runId, ScanPhase phase, CancellationToken cancellationToken = default)
    {
        await db.ScanRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Phase, phase), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CompleteAsync(long runId, ScanPhase terminal, string? error = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        await db.ScanRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Phase, terminal)
                .SetProperty(r => r.CompletedAt, now)
                .SetProperty(r => r.Error, error), cancellationToken)
            .ConfigureAwait(false);
    }

    public Task UpsertDiscoveryAsync(long runId, Guid repositoryId, ScannedVar scanned, CancellationToken cancellationToken = default) =>
        UpsertDiscoveryBatchAsync(runId, repositoryId, [scanned], cancellationToken);

    public async Task UpsertDiscoveryBatchAsync(
        long runId,
        Guid repositoryId,
        IReadOnlyList<ScannedVar> scanned,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(scanned);
        if (scanned.Count == 0)
            return;

        var generation = await db.ScanRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => r.Generation)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        var paths = scanned.Select(s => s.RelativePath).ToList();
        var existingByPath = await db.VarFiles
            .Where(v => v.RepositoryId == repositoryId && paths.Contains(v.RelativePath))
            .ToDictionaryAsync(v => v.RelativePath, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);
        long skipped = 0;
        var now = clock.UtcNow.UtcDateTime;

        foreach (var item in scanned)
        {
            var found = existingByPath.TryGetValue(item.RelativePath, out var existing);
            var fresh = found &&
                        RepositoryScanRules.IsFresh(existing!.SizeBytes, existing.FileMtime, item.SizeBytes, item.FileMtimeUtc);

            if (!found)
            {
                existing = new VarFile
                {
                    RepositoryId = repositoryId,
                    RelativePath = item.RelativePath,
                    IngestState = IngestState.Discovered,
                };
                db.VarFiles.Add(existing);
            }

            existing!.SizeBytes = item.SizeBytes;
            existing.FileMtime = item.FileMtimeUtc;
            existing.QuarantineKind = item.Quarantine;
            existing.SeenGeneration = generation;

            if (!fresh || existing.IngestState != IngestState.RawStored)
            {
                if (existing.IngestState != IngestState.Inspecting ||
                    existing.LeaseExpiresAt is null ||
                    existing.LeaseExpiresAt < now)
                {
                    existing.IngestState = IngestState.Discovered;
                    existing.LeaseOwner = null;
                    existing.LeaseExpiresAt = null;
                    existing.IngestAttempts = 0;
                    existing.IngestError = null;
                }
            }
            else
            {
                skipped++;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await db.ScanRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Discovered, r => r.Discovered + scanned.Count)
                .SetProperty(r => r.Skipped, r => r.Skipped + skipped), cancellationToken)
            .ConfigureAwait(false);
        db.ChangeTracker.Clear();
    }

    public async Task<int> MarkVanishedAsync(long runId, Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var gen = await db.ScanRuns.Where(r => r.Id == runId).Select(r => r.Generation).FirstAsync(cancellationToken).ConfigureAwait(false);
        // Callers prune separately when online; here we only count not-seen for telemetry.
        return await db.VarFiles.CountAsync(
            v => v.RepositoryId == repositoryId && v.SeenGeneration != gen, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReclaimExpiredLeasesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        await db.VarFiles
            .Where(v => v.IngestState == IngestState.Inspecting && v.LeaseExpiresAt != null && v.LeaseExpiresAt < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.IngestState, IngestState.Discovered)
                .SetProperty(v => v.LeaseOwner, (Guid?)null)
                .SetProperty(v => v.LeaseExpiresAt, (DateTime?)null), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<long>> ClaimWorkAsync(
        long runId, Guid repositoryId, Guid leaseOwner, int take, CancellationToken cancellationToken = default)
    {
        await ReclaimExpiredLeasesAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow.UtcDateTime;
        var expires = now + IngestLimits.LeaseDuration;
        var gen = await db.ScanRuns.Where(r => r.Id == runId)
            .Select(r => r.Generation)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        // Only Discovered + this generation. Claiming Failed would re-queue forever inside one run
        // (producer loops until Claim returns empty). Failed retries on the next discovery pass.
        var candidates = await db.VarFiles
            .Where(v => v.RepositoryId == repositoryId &&
                        v.SeenGeneration == gen &&
                        v.IngestState == IngestState.Discovered &&
                        v.IngestAttempts < IngestLimits.MaxIngestAttempts)
            .OrderBy(v => v.Id)
            .Take(take)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
            return [];

        await db.VarFiles.Where(v => candidates.Contains(v.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.IngestState, IngestState.Inspecting)
                .SetProperty(v => v.LeaseOwner, leaseOwner)
                .SetProperty(v => v.LeaseExpiresAt, expires)
                .SetProperty(v => v.IngestAttempts, v => v.IngestAttempts + 1), cancellationToken)
            .ConfigureAwait(false);

        return candidates;
    }

    public async Task MarkRawStoredAsync(long varFileId, CancellationToken cancellationToken = default)
    {
        await db.VarFiles.Where(v => v.Id == varFileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.IngestState, IngestState.RawStored)
                .SetProperty(v => v.LeaseOwner, (Guid?)null)
                .SetProperty(v => v.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(v => v.IngestError, (string?)null), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(long varFileId, string error, CancellationToken cancellationToken = default)
    {
        await db.VarFiles.Where(v => v.Id == varFileId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(v => v.IngestState, IngestState.Failed)
                .SetProperty(v => v.LeaseOwner, (Guid?)null)
                .SetProperty(v => v.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(v => v.IngestError, error), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CheckpointWalAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);", cancellationToken).ConfigureAwait(false);
    }

    public async Task AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("ANALYZE;", cancellationToken).ConfigureAwait(false);
    }

    public Task<ScanRun?> GetRunAsync(long runId, CancellationToken cancellationToken = default) =>
        db.ScanRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);

    public async Task SetSignatureAsync(long runId, RepositorySignature signature, CancellationToken cancellationToken = default)
    {
        await db.ScanRuns.Where(r => r.Id == runId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.SigFileCount, signature.FileCount)
                .SetProperty(r => r.SigTotalBytes, signature.TotalBytes)
                .SetProperty(r => r.SigNewestMtimeTicks, signature.NewestMtimeTicks)
                .SetProperty(r => r.SigPathsHash, signature.PathsHash), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RepositorySignature?> GetLastCompletedSignatureAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var row = await db.ScanRuns.AsNoTracking()
            .Where(r => r.RepositoryId == repositoryId
                        && r.Phase == ScanPhase.Completed
                        && r.SigPathsHash != null)
            .OrderByDescending(r => r.Generation)
            .Select(r => new { r.SigFileCount, r.SigTotalBytes, r.SigNewestMtimeTicks, r.SigPathsHash })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return null;
        return new RepositorySignature(
            row.SigFileCount ?? 0, row.SigTotalBytes ?? 0, row.SigNewestMtimeTicks ?? 0, row.SigPathsHash ?? 0);
    }

    public Task<bool> HasPendingWorkAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
        db.VarFiles.AsNoTracking()
            .AnyAsync(v => v.RepositoryId == repositoryId && v.IngestState != IngestState.RawStored, cancellationToken);
}
