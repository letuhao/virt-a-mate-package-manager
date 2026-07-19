using Microsoft.Extensions.DependencyInjection;
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
    public async Task<IndexRunSummary> IndexAllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        var repos = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r is { IsOnline: true, IsEnabled: true })
            .ToList();

        int indexed = 0, skipped = 0, pruned = 0;
        foreach (var repo in repos)
        {
            var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, cancellationToken).ConfigureAwait(false);
            indexed += r.Indexed; skipped += r.Skipped; pruned += r.Pruned;
        }

        return await ResolveAndComputeAsync(scope.ServiceProvider, repos.Count, indexed, skipped, pruned, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IndexRunSummary> IndexRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var repositories = scope.ServiceProvider.GetRequiredService<IRepositoryService>();

        var repo = (await repositories.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.Id == repositoryId);
        if (repo is null)
            return new IndexRunSummary(0, 0, 0, 0, 0, 0, 0);

        var r = await indexer.IndexRepositoryAsync(repo.Id, repo.MountPath, cancellationToken).ConfigureAwait(false);
        return await ResolveAndComputeAsync(scope.ServiceProvider, 1, r.Indexed, r.Skipped, r.Pruned, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IndexRunSummary> ResolveAndComputeAsync(
        IServiceProvider services, int repoCount, int indexed, int skipped, int pruned, CancellationToken cancellationToken)
    {
        // Dependency resolution populates HasMissingDeps + reverse counts (the piece with no prod caller).
        var resolution = await services.GetRequiredService<IDependencyResolver>()
            .ResolveAllAsync(cancellationToken).ConfigureAwait(false);
        var usageRecomputed = await services.GetRequiredService<IUsageAnalyzer>()
            .RecomputeAsync(cancellationToken).ConfigureAwait(false);

        return new IndexRunSummary(
            repoCount, indexed, skipped, pruned,
            resolution.Resolved, resolution.Missing, usageRecomputed);
    }
}
