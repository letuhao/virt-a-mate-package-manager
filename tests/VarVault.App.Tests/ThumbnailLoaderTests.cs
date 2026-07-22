using System.Collections.Concurrent;
using VarVault.App.Services;
using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// The thumbnail loader decodes off the calling (UI) thread, memoizes decoded images, and prefetches
/// a look-ahead window. (Checklist 1.35.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ThumbnailLoaderTests
{
    [Fact]
    public async Task Decode_runs_off_the_calling_thread()
    {
        var callingThread = Environment.CurrentManagedThreadId;
        var decodeThread = 0;
        var loader = new ThumbnailLoader<string>(
            new StubStore(id => [1, 2, 3]),
            bytes => { decodeThread = Environment.CurrentManagedThreadId; return "img"; });

        var image = await loader.LoadAsync(7);

        Assert.Equal("img", image);
        Assert.NotEqual(callingThread, decodeThread); // decode was pushed to a pool thread
    }

    [Fact]
    public async Task Repeated_loads_decode_once_and_serve_from_cache()
    {
        var decodes = 0;
        var loader = new ThumbnailLoader<string>(
            new StubStore(id => [0]),
            _ => { Interlocked.Increment(ref decodes); return "img"; });

        await loader.LoadAsync(1);
        await loader.LoadAsync(1);
        await loader.LoadAsync(1);

        Assert.Equal(1, decodes); // memoized
    }

    [Fact]
    public async Task Missing_thumbnail_returns_null_without_decoding()
    {
        var decoded = false;
        var loader = new ThumbnailLoader<string>(
            new StubStore(_ => null),
            _ => { decoded = true; return "img"; });

        Assert.Null(await loader.LoadAsync(42));
        Assert.False(decoded);
    }

    [Fact]
    public async Task Prefetch_warms_the_cache_for_a_window()
    {
        var loaded = new ConcurrentBag<long>();
        var loader = new ThumbnailLoader<string>(
            new StubStore(id => { loaded.Add(id); return [0]; }),
            _ => "img");

        await loader.PrefetchAsync([10, 11, 12, 13]);

        Assert.Equal(4, loaded.Distinct().Count());
        // A subsequent load is served from cache — the store isn't hit again.
        await loader.LoadAsync(11);
        Assert.Equal(4, loaded.Count);
    }

    private sealed class StubStore(Func<long, byte[]?> bytesFor) : IThumbnailStore
    {
        public Task<byte[]?> GetAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bytesFor(packageId));
        public Task PutAsync(long packageId, byte[] jpeg, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task<bool> ExistsAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(bytesFor(packageId) is not null);
        public Task PutContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<byte[]?> GetContentAsync(long contentItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
        public Task PutFocusContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<byte[]?> GetFocusContentAsync(long contentItemId, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);
    }
}
