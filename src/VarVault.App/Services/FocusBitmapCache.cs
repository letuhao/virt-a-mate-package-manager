using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace VarVault.App.Services;

/// <summary>
/// LRU cache of decoded focus-viewer bitmaps. Owns dispose of entries; separate from the 384px wall
/// thumbnail path so wide-sidebar focus images do not thrash the packed thumb store.
/// </summary>
public sealed class FocusBitmapCache(int capacity = 8) : IDisposable
{
    private readonly int _capacity = Math.Max(1, capacity);
    private readonly LinkedList<long> _order = new();
    private readonly Dictionary<long, LinkedListNode<long>> _nodes = new();
    private readonly Dictionary<long, Bitmap> _bitmaps = new();
    private bool _disposed;

    public int Count => _bitmaps.Count;

    public bool TryGet(long contentItemId, out Bitmap? bitmap)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bitmaps.TryGetValue(contentItemId, out bitmap) && bitmap is not null)
        {
            Touch(contentItemId);
            return true;
        }
        bitmap = null;
        return false;
    }

    /// <summary>Content item currently shown in the focus viewer — never disposed by LRU trim.</summary>
    public long? PinnedId { get; set; }

    /// <summary>Insert or replace. Takes ownership of <paramref name="bitmap"/>.</summary>
    public void Set(long contentItemId, Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_bitmaps.TryGetValue(contentItemId, out var existing))
        {
            if (!ReferenceEquals(existing, bitmap))
                existing.Dispose();
            _bitmaps[contentItemId] = bitmap;
            Touch(contentItemId);
            return;
        }

        _bitmaps[contentItemId] = bitmap;
        _nodes[contentItemId] = _order.AddFirst(contentItemId);
        Trim();
    }

    public void Remove(long contentItemId)
    {
        if (_disposed)
            return;
        if (!_bitmaps.Remove(contentItemId, out var bitmap))
            return;
        bitmap.Dispose();
        if (_nodes.Remove(contentItemId, out var node))
            _order.Remove(node);
        if (PinnedId == contentItemId)
            PinnedId = null;
    }

    public void Clear()
    {
        if (_disposed)
            return;
        foreach (var bitmap in _bitmaps.Values)
            bitmap.Dispose();
        _bitmaps.Clear();
        _nodes.Clear();
        _order.Clear();
        PinnedId = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _disposed = true;
    }

    private void Touch(long contentItemId)
    {
        if (!_nodes.TryGetValue(contentItemId, out var node))
            return;
        _order.Remove(node);
        _nodes[contentItemId] = _order.AddFirst(contentItemId);
    }

    private void Trim()
    {
        while (_bitmaps.Count > _capacity && _order.Last is { } last)
        {
            var id = last.Value;
            // Never dispose the on-screen focus image. Soft-overflow if it's the only victim left.
            if (PinnedId == id)
            {
                Touch(id);
                if (_order.Last?.Value == id)
                    break;
                continue;
            }
            _order.RemoveLast();
            _nodes.Remove(id);
            if (_bitmaps.Remove(id, out var bitmap))
                bitmap.Dispose();
        }
    }
}
