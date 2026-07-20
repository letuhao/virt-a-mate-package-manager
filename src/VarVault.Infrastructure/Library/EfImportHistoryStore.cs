using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Import;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>Persists + reads import runs (History). Writes go through the single-writer queue; the run list is
/// pruned to the most recent <c>import.history_keep</c> (default 200). (doc 30 §8; doc 31 Phase 5.6/5.10.)</summary>
public sealed class EfImportHistoryStore(VarVaultDbContext db, IWriteQueue writeQueue, ISettingsService settings)
    : IImportHistoryStore
{
    private const string KeepKey = "import.history_keep";
    private const int DefaultKeep = 200;

    public Task<Guid> RecordAsync(ImportRun run, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueAsync(async ct =>
        {
            var entity = new ImportRunEntity
            {
                Id = run.Id == default ? Guid.NewGuid() : run.Id,
                StartedUtc = run.StartedUtc,
                TargetRepositoryId = run.TargetRepositoryId,
                SourceSummary = run.SourceSummary,
                Copied = run.Copied, Fixed = run.Fixed, Renamed = run.Renamed,
                Skipped = run.Skipped, Discarded = run.Discarded, Failed = run.Failed,
            };
            foreach (var f in run.FailedSources)
                entity.FailedSources.Add(new ImportFailedSourceEntity { Path = f.Path, Kind = f.Kind.ToString(), Reason = f.Reason });
            foreach (var o in run.Outcomes)
                entity.Outcomes.Add(new ImportOutcomeEntity
                {
                    FileName = o.FileName, IdentityKey = o.IdentityKey, Lane = o.Lane.ToString(),
                    Decision = o.Decision.ToString(), Result = o.Ok ? "ok" : "failed", Reason = o.Reason,
                });
            db.ImportRuns.Add(entity);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Prune oldest beyond the keep limit. (5.10)
            var keep = await ResolveKeepAsync(ct).ConfigureAwait(false);
            var stale = await db.ImportRuns.OrderByDescending(r => r.StartedUtc).Skip(keep).ToListAsync(ct).ConfigureAwait(false);
            if (stale.Count > 0)
            {
                db.ImportRuns.RemoveRange(stale);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return entity.Id;
        }, WritePriority.Normal, cancellationToken);

    public async Task<IReadOnlyList<ImportRun>> RecentAsync(int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.ImportRuns.Include(r => r.FailedSources).Include(r => r.Outcomes)
            .OrderByDescending(r => r.StartedUtc)
            .Take(take)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToList();
    }

    private async Task<int> ResolveKeepAsync(CancellationToken ct)
    {
        var raw = await settings.GetAsync(KeepKey, ct).ConfigureAwait(false);
        return int.TryParse(raw, out var k) && k > 0 ? k : DefaultKeep;
    }

    private static ImportRun Map(ImportRunEntity e) => new(
        e.Id, e.StartedUtc, e.TargetRepositoryId, e.SourceSummary,
        e.Copied, e.Fixed, e.Renamed, e.Skipped, e.Discarded, e.Failed,
        e.FailedSources.Select(f => new ImportFailedSource(
            f.Path, Enum.TryParse<ImportSourceKind>(f.Kind, out var kind) ? kind : ImportSourceKind.Archive, f.Reason)).ToList(),
        e.Outcomes.Select(o => new ImportOutcome(
            o.FileName, o.IdentityKey,
            Enum.TryParse<ImportLane>(o.Lane, out var lane) ? lane : ImportLane.New,
            Enum.TryParse<ImportDecision>(o.Decision, out var dec) ? dec : ImportDecision.None,
            o.Result == "ok", o.Reason)).ToList());
}
