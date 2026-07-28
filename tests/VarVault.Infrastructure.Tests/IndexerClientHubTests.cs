using VarVault.Common;
using VarVault.Infrastructure.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class IndexerClientHubTests
{
    [Fact]
    public async Task Redirect_routes_StartIndexRepository_to_the_new_target()
    {
        var fallback = new FakeIndexerClient("fallback");
        var pipe = new FakeIndexerClient("pipe");
        var hub = new IndexerClientHub(fallback);

        var before = await hub.StartIndexRepositoryAsync(Guid.NewGuid());
        Assert.True(before.IsSuccess);
        Assert.Equal(1, fallback.StartRepoCalls);
        Assert.Equal(0, pipe.StartRepoCalls);

        hub.Redirect(pipe);
        var after = await hub.StartIndexRepositoryAsync(Guid.NewGuid());
        Assert.True(after.IsSuccess);
        Assert.Equal(1, fallback.StartRepoCalls);
        Assert.Equal(1, pipe.StartRepoCalls);
    }

    private sealed class FakeIndexerClient(string name) : IIndexerClient
    {
        public int StartRepoCalls { get; private set; }
        public string Name { get; } = name;

        public Task<Result<IndexerStatus>> PingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Idle()));

        public Task<Result<Guid>> StartIndexAllAsync(bool forceFull = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Guid.NewGuid()));

        public Task<Result<Guid>> StartIndexRepositoryAsync(
            Guid repositoryId, bool forceFull = false, CancellationToken cancellationToken = default)
        {
            StartRepoCalls++;
            return Task.FromResult(Result.Success(Guid.NewGuid()));
        }

        public Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result<IndexerStatus>> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(Idle()));

        public Task<Result> RegisterOwnerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        public Task<Result> UnregisterOwnerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());

        private static IndexerStatus Idle() =>
            new(IndexerProtocol.Version, IndexerJobState.Idle, null, null, 0, 0, 0, 0, 0, 0, null);
    }
}
