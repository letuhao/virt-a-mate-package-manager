using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Dependencies;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;

namespace VarVault.Modules.Indexing;

/// <summary>
/// BE-N0 · Index orchestration. Indexes the enabled+online repositories, then resolves dependencies and
/// recomputes usage — the runtime trigger that makes the read model complete (previously indexing only ran
/// under test). Persistence-backed services are resolved per call via a scope (like
/// <see cref="IndexingService"/>) so the composition root stays valid without persistence. (16-checklist BE-N0.)
/// </summary>
internal sealed class IndexOrchestrator(IServiceScopeFactory scopeFactory, IIndexingService indexer) : IIndexOrchestrator
{
    public async Task<IndexRunSummary> IndexAllAsync(IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        var repos = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r is { IsOnline: true, IsEnabled: true })
            .ToList();

        int indexed = 0, skipped = 0, pruned = 0;
        for (var i = 0; i < repos.Count; i++)
        {
            var repo = repos[i];
            // A repo's live scan/inspect progress flows straight into the job handle. With multiple repos each
            // bar is per-repo (denominators aren't known until each repo's scan finishes) — the "Repo i/N"
            // prefix keeps the run legible; a single-repo library (the common case) shows one exact bar.
            var repoProgress = repos.Count > 1 ? new PrefixProgress(progress, $"Repo {i + 1}/{repos.Count} · ") : progress;
            var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, repoProgress, cancellationToken).ConfigureAwait(false);
            indexed += r.Indexed; skipped += r.Skipped; pruned += r.Pruned;
        }

        return await ResolveAndComputeAsync(scope.ServiceProvider, repos.Count, indexed, skipped, pruned, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexRunSummary> IndexRepositoryAsync(Guid repositoryId, IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        var repo = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null)
            return new IndexRunSummary(0, 0, 0, 0, 0, 0, 0);

        var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, progress, cancellationToken).ConfigureAwait(false);
        return await ResolveAndComputeAsync(scope.ServiceProvider, 1, r.Indexed, r.Skipped, r.Pruned, progress, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IndexRunSummary> ResolveAndComputeAsync(
        IServiceProvider services, int repoCount, int indexed, int skipped, int pruned,
        IProgressSink progress, CancellationToken cancellationToken)
    {
        // These two post-passes were previously invisible (they can take a while on a big catalog); report them
        // as indeterminate phases so the status line doesn't look stalled after inspection finishes.
        progress.Report(new ProgressReport(0, 0, "Resolving dependencies…"));
        // Dependency resolution populates HasMissingDeps + reverse counts (the piece with no prod caller).
        var resolution = await services.GetRequiredService<IDependencyResolver>()
            .ResolveAllAsync(cancellationToken).ConfigureAwait(false);

        progress.Report(new ProgressReport(0, 0, "Computing usage…"));
        var usageRecomputed = await services.GetRequiredService<IUsageAnalyzer>()
            .RecomputeAsync(cancellationToken).ConfigureAwait(false);

        return new IndexRunSummary(
            repoCount, indexed, skipped, pruned,
            resolution.Resolved, resolution.Missing, usageRecomputed);
    }

    /// <summary>Prefixes a repo's status message so a multi-repo run stays legible; passes counts through unchanged.</summary>
    private sealed class PrefixProgress(IProgressSink inner, string prefix) : IProgressSink
    {
        public void Report(ProgressReport report) =>
            inner.Report(report with { Message = prefix + report.Message });
    }
}
