using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>One wall-tile in the package content gallery (lazy thumbnail load).</summary>
public sealed partial class GalleryThumbViewModel(
    ContentItemDto item,
    long packageId,
    IThumbnailStore? thumbnails,
    IIndexerClient? indexer) : ObservableObject
{
    private static readonly SemaphoreSlim Concurrency = new(4);
    private int _loadGeneration;

    public ContentItemDto Item => item;
    public long ContentItemId => item.ContentItemId;
    public string Type => item.Type;
    public string EntryPath => item.EntryPath;
    public string ShortName
    {
        get
        {
            var name = Path.GetFileNameWithoutExtension(item.EntryPath.Replace('\\', '/'));
            return string.IsNullOrWhiteSpace(name) ? item.Type : name;
        }
    }
    public bool IsPreset => item.IsPreset;
    public bool CanPreview => item.HasPreview;

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isFallback;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _previewStatus;

    public bool HasThumbnail => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (thumbnails is null || HasThumbnail)
            return;
        var generation = ++_loadGeneration;
        IsLoading = true;
        try
        {
            await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            if (generation == _loadGeneration)
                IsLoading = false;
            return;
        }

        try
        {
            if (generation != _loadGeneration)
                return;

            var bytes = await thumbnails.GetContentAsync(item.ContentItemId, cancellationToken).ConfigureAwait(false);
            if (bytes is null && item.HasPreview && indexer is not null)
            {
                var extracted = await indexer.ExtractContentPreviewAsync(item.ContentItemId, cancellationToken).ConfigureAwait(false);
                if (extracted.IsSuccess)
                    bytes = await thumbnails.GetContentAsync(item.ContentItemId, cancellationToken).ConfigureAwait(false);
                else
                    PreviewStatus = extracted.Error.Message;
            }

            if (bytes is null)
            {
                bytes = await thumbnails.GetAsync(packageId, cancellationToken).ConfigureAwait(false);
                IsFallback = bytes is not null;
            }

            if (generation != _loadGeneration)
                return;

            if (bytes is not null)
            {
                var bitmap = await Task.Run(() => new Bitmap(new MemoryStream(bytes)), cancellationToken).ConfigureAwait(true);
                if (generation != _loadGeneration)
                {
                    bitmap.Dispose();
                    return;
                }
                Thumbnail?.Dispose();
                Thumbnail = bitmap;
                // Invalidate may have raced between the check and assign — drop a stale bitmap.
                if (generation != _loadGeneration)
                {
                    Thumbnail?.Dispose();
                    Thumbnail = null;
                }
            }
            else
            {
                PreviewStatus ??= item.HasPreview ? "Preview unavailable" : "No preview";
            }
        }
        catch (OperationCanceledException)
        {
            // leave retryable
        }
        catch (Exception)
        {
            if (generation == _loadGeneration)
                PreviewStatus = "Preview unavailable";
        }
        finally
        {
            Concurrency.Release();
            if (generation == _loadGeneration)
                IsLoading = false;
        }
    }

    public void Invalidate()
    {
        _loadGeneration++;
        IsLoading = false;
        Thumbnail?.Dispose();
        Thumbnail = null;
        IsFallback = false;
        PreviewStatus = null;
    }
}
