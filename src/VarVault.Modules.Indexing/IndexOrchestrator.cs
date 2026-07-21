using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;

namespace VarVault.Modules.Indexing;

/// <summary>
/// Indexes enabled+online repositories via the bounded stream indexer, then resolves dependencies
/// and recomputes usage. (A12 raw-first path; BE-N0.)
/// </summary>
internal sealed class IndexOrchestrator(IServiceScopeFactory scopeFactory) : IIndexOrchestrator
{
    public async Task<IndexRunSummary> IndexAllAsync(IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();

        var repos = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r is { IsOnline: true, IsEnabled: true })
            .ToList();

        int indexed = 0, skipped = 0, pruned = 0;
        for (var i = 0; i < repos.Count; i++)
        {
            var repo = repos[i];
            var media = Enum.TryParse<MediaType>(repo.MediaType, ignoreCase: true, out var mt) ? mt : MediaType.Unknown;
            var repoProgress = repos.Count > 1 ? new PrefixProgress(progress, $"Repo {i + 1}/{repos.Count} · ") : progress;
            var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, media, repoProgress, cancellationToken: cancellationToken).ConfigureAwait(false);
            indexed += r.Indexed; skipped += r.Skipped; pruned += r.Pruned;
        }

        return await ResolveAndComputeAsync(scope.ServiceProvider, repos.Count, indexed, skipped, pruned, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexRunSummary> IndexRepositoryAsync(Guid repositoryId, IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var indexer = scope.ServiceProvider.GetRequiredService<IStreamIndexer>();

        var repo = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null)
            return new IndexRunSummary(0, 0, 0, 0, 0, 0, 0);

        var media = Enum.TryParse<MediaType>(repo.MediaType, ignoreCase: true, out var mt) ? mt : MediaType.Unknown;
        var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, media, progress, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await ResolveAndComputeAsync(scope.ServiceProvider, 1, r.Indexed, r.Skipped, r.Pruned, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IndexRunSummary> ResolveAndComputeAsync(
        IServiceProvider services, int repoCount, int indexed, int skipped, int pruned,
        IProgressSink progress, CancellationToken cancellationToken)
    {
        progress.Report(new ProgressReport(0, 0, "Resolving dependencies…"));
        var resolution = await services.GetRequiredService<Domain.Dependencies.IDependencyResolver>()
            .ResolveAllAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(new ProgressReport(0, 0, "Computing usage…"));
        var usageRecomputed = await services.GetRequiredService<Domain.Analyzer.IUsageAnalyzer>()
            .RecomputeAsync(cancellationToken).ConfigureAwait(false);

        return new IndexRunSummary(
            repoCount, indexed, skipped, pruned,
            resolution.Resolved, resolution.Missing, usageRecomputed);
    }

    private sealed class PrefixProgress(IProgressSink inner, string prefix) : IProgressSink
    {
        public void Report(ProgressReport report) =>
            inner.Report(report with { Message = prefix + report.Message });
    }
}
