using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

/// <summary>The dirty-set collects packages whose read-model rows a base-table write invalidated. (Checklist 0.26.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ReadModelDirtySetTests
{
    [Fact]
    public void Marking_dedups_and_drain_clears()
    {
        var set = new ReadModelDirtySet();
        set.MarkPackage(1);
        set.MarkPackage(1); // same package again — one dirty entry
        set.MarkPackage(2);
        Assert.Equal(2, set.Count);

        var batch = set.Drain();
        Assert.Equal([1, 2], batch.Order());
        Assert.Equal(0, set.Count);       // draining clears
        Assert.Empty(set.Drain());        // second drain is empty
    }

    [Fact]
    public void Null_package_id_is_ignored()
    {
        var set = new ReadModelDirtySet();
        set.MarkPackage((long?)null); // e.g. an unrecognized var with no package
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public async Task Flush_refreshes_exactly_the_dirty_packages_then_clears()
    {
        var store = new RecordingStore();
        var set = new ReadModelDirtySet();
        set.MarkPackage(7);
        set.MarkPackage(9);

        await set.FlushAsync(store);

        Assert.Equal([7, 9], store.Refreshed.Order());
        Assert.Equal(0, set.Count);

        // A flush with nothing dirty is a no-op (doesn't hit the store).
        store.Refreshed.Clear();
        await set.FlushAsync(store);
        Assert.Empty(store.Refreshed);
    }

    private sealed class RecordingStore : ICatalogStore
    {
        public List<long> Refreshed { get; } = [];

        public Task RefreshReadModelAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default)
        {
            Refreshed.AddRange(packageIds);
            return Task.CompletedTask;
        }

        public Task<bool> RepositoryIsOnlineAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<IReadOnlyList<Domain.Indexing.ExistingVarFile>> ListVarFilesAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Domain.Indexing.ExistingVarFile>>([]);
        public Task<long?> ApplyAsync(Domain.Indexing.VarUpsert upsert, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(null);
        public Task<int> RemoveVarFilesAsync(IReadOnlyCollection<long> varFileIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
