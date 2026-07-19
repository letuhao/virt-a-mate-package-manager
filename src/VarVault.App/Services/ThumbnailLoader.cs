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
public sealed class ThumbnailLoader<TImage>(IThumbnailStore store, Func<byte[], TImage> decode)
    where TImage : class
{
    private readonly ConcurrentDictionary<long, Task<TImage?>> _cache = new();

    /// <summary>
    /// The decoded thumbnail for a package (or null when none is stored). Concurrent callers for the
    /// same id share one decode; a completed decode is served from cache without touching disk again.
    /// </summary>
    public Task<TImage?> LoadAsync(long packageId, CancellationToken cancellationToken = default) =>
        _cache.GetOrAdd(packageId, id => LoadCoreAsync(id));

    /// <summary>Warm the cache for a run of ids (e.g. the rows just below the viewport). (1.35 prefetch.)</summary>
    public Task PrefetchAsync(IEnumerable<long> packageIds, CancellationToken cancellationToken = default) =>
        Task.WhenAll(packageIds.Select(id => LoadAsync(id, cancellationToken)));

    private async Task<TImage?> LoadCoreAsync(long packageId)
    {
        try
        {
            // Bytes come off the store's async I/O; decoding is CPU work pushed to a pool thread so the
            // UI thread is never the one turning JPEG into pixels.
            var bytes = await store.GetAsync(packageId).ConfigureAwait(false);
            if (bytes is null)
                return null;
            return await Task.Run(() => decode(bytes)).ConfigureAwait(false);
        }
        catch
        {
            // Don't poison the cache with a faulted task — a later load may succeed (e.g. after re-index).
            _cache.TryRemove(packageId, out _);
            throw;
        }
    }
}

/// <summary>Production factory: decodes stored JPEG bytes into an Avalonia <see cref="Bitmap"/>.</summary>
public static class ThumbnailLoader
{
    public static ThumbnailLoader<Bitmap> ForBitmap(IThumbnailStore store) =>
        new(store, bytes => new Bitmap(new MemoryStream(bytes)));
}
