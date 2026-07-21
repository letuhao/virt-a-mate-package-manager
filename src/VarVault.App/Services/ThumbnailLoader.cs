using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using VarVault.Domain.Indexing;

namespace VarVault.App.Services;

/// <summary>
/// Loads and decodes package thumbnails <b>off the UI thread</b>, memoizing decoded images and
/// supporting look-ahead prefetch so scrolling the gallery never blocks the dispatcher. The decode
/// step is a delegate so the pipeline is testable without an image backend. (Checklist 1.35.)
/// </summary>
/// <typeparam name="TImage">The decoded image type (Avalonia <see cref="Bitmap"/> in production).</typeparam>
public sealed class ThumbnailLoader<TImage>(
    IThumbnailStore store,
    Func<byte[], TImage> decode,
    int maxCached = 512,
    int maxConcurrency = 4)
    where TImage : class
{
    private readonly ConcurrentDictionary<long, Task<TImage?>> _cache = new();
    private readonly ConcurrentQueue<long> _order = new();
    private readonly int _maxCached = Math.Max(32, maxCached);
    private readonly SemaphoreSlim _concurrency = new(Math.Max(1, maxConcurrency));

    public Task<TImage?> LoadAsync(long packageId, CancellationToken cancellationToken = default) =>
        _cache.GetOrAdd(packageId, id =>
        {
            _order.Enqueue(id);
            TrimIfNeeded();
            return LoadCoreAsync(id);
        });

    public Task PrefetchAsync(IEnumerable<long> packageIds, CancellationToken cancellationToken = default) =>
        Task.WhenAll(packageIds.Select(id => LoadAsync(id, cancellationToken)));

    private void TrimIfNeeded()
    {
        while (_cache.Count > _maxCached && _order.TryDequeue(out var old))
            _cache.TryRemove(old, out _);
    }

    private async Task<TImage?> LoadCoreAsync(long packageId)
    {
        await _concurrency.WaitAsync().ConfigureAwait(false);
        try
        {
            var bytes = await store.GetAsync(packageId).ConfigureAwait(false);
            if (bytes is null)
                return null;
            return await Task.Run(() => decode(bytes)).ConfigureAwait(false);
        }
        catch
        {
            _cache.TryRemove(packageId, out _);
            throw;
        }
        finally
        {
            _concurrency.Release();
        }
    }
}

/// <summary>Production factory: decodes stored JPEG bytes into an Avalonia <see cref="Bitmap"/>.</summary>
public static class ThumbnailLoader
{
    public static ThumbnailLoader<Bitmap> ForBitmap(IThumbnailStore store) =>
        new(store, bytes => new Bitmap(new MemoryStream(bytes)));
}
