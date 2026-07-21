using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>One content-gallery item with an on-demand worker-extracted preview.</summary>
public sealed partial class ContentCardViewModel(
    ContentItemDto item,
    long packageId,
    IThumbnailStore? thumbnails,
    IIndexerClient? indexer) : ObservableObject
{
    private static readonly SemaphoreSlim Concurrency = new(4);
    private bool _loaded;

    public ContentItemDto Item => item;
    public long ContentItemId => item.ContentItemId;
    public string Type => item.Type;
    public string EntryPath => item.EntryPath;
    public bool IsPreset => item.IsPreset;

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private bool _isFallback;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _previewStatus;

    public bool HasThumbnail => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded || thumbnails is null)
            return;
        _loaded = true;
        IsLoading = true;
        await Concurrency.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
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

            if (bytes is not null)
                Thumbnail = await Task.Run(() => new Bitmap(new MemoryStream(bytes)), cancellationToken).ConfigureAwait(true);
            else
                PreviewStatus ??= item.HasPreview ? "Preview unavailable" : "No preview for this content type";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PreviewStatus = "Preview unavailable";
        }
        finally
        {
            Concurrency.Release();
            IsLoading = false;
        }
    }
}
