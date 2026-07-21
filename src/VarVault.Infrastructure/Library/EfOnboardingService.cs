using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N13 · Onboarding. Registers the folder (media/tier detected by <see cref="IRepositoryService"/>),
/// applies the reserve, then indexes through the indexer worker (not a direct orchestrator call). (A12.)
/// </summary>
public sealed class EfOnboardingService(
    VarVaultDbContext db,
    IRepositoryService repositories,
    IIndexerClient indexer) : IOnboardingService
{
    public async Task<Result<OnboardingResult>> AddAndIndexAsync(string name, string path, long? reserveBytes = null, CancellationToken cancellationToken = default)
    {
        var registered = await repositories.RegisterAsync(new RegisterRepositoryRequest(name, path), cancellationToken).ConfigureAwait(false);
        if (registered.IsFailure)
            return Result.Failure<OnboardingResult>(registered.Error.Code, registered.Error.Message);

        if (reserveBytes is { } reserve)
        {
            var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == registered.Value.Id, cancellationToken).ConfigureAwait(false);
            if (repo is not null)
            {
                repo.MinFreeBytes = reserve;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        var start = await indexer.StartIndexRepositoryAsync(registered.Value.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (start.IsFailure)
            return Result.Failure<OnboardingResult>(start.Error.Code, start.Error.Message);

        IndexerStatus? last = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await indexer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsFailure)
                return Result.Failure<OnboardingResult>(status.Error.Code, status.Error.Message);
            last = status.Value;
            if (last.State is IndexerJobState.Completed or IndexerJobState.Failed
                or IndexerJobState.Cancelled or IndexerJobState.Idle)
                break;
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (last is null)
            return Result.Failure<OnboardingResult>("indexer.status", "no status");
        if (last.State == IndexerJobState.Failed)
            return Result.Failure<OnboardingResult>("indexer.failed", last.Error ?? "index failed");
        if (last.State == IndexerJobState.Cancelled)
            return Result.Failure<OnboardingResult>("indexer.cancelled", "index cancelled");

        var summary = new IndexRunSummary(
            Repositories: 1,
            Indexed: (int)Math.Min(int.MaxValue, last.Done),
            Skipped: 0,
            Pruned: 0,
            Resolved: 0,
            Missing: 0,
            UsageRecomputed: 0);
        return Result.Success(new OnboardingResult(registered.Value, summary));
    }
}
