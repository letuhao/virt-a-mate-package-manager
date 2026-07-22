using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>Gallery wall vs single-image focus viewer inside the package inspector.</summary>
public enum PackageGalleryMode
{
    Gallery,
    Focus,
}

/// <summary>
/// Shared package content gallery: lazy paged wall + focus viewer. Used by the Library sidebar and
/// the Var detail Content tab. Clears immediately on package change; cancels in-flight loads.
/// </summary>
public sealed partial class PackageGalleryViewModel : ObservableObject, IDisposable
{
    public const double MinPanelWidth = 280;

    private const string PrefTypeFilter = "library.gallery.type";
    private const string PrefLoadableOnly = "library.gallery.loadable_only";

    private readonly IPackageDetailQuery? _detail;
    private readonly IThumbnailStore? _thumbnails;
    private readonly IIndexerClient? _indexer;
    private readonly ISettingsService? _settings;
    private readonly FocusBitmapCache _focusCache = new(8);
    private CancellationTokenSource? _bindCts;
    private CancellationTokenSource? _focusCts;
    private int _bindGeneration;
    private int _focusGeneration;
    private bool _disposed;
    private bool _suppressFilterPersist;

    public PackageGalleryViewModel(
        IPackageDetailQuery? detail = null,
        IThumbnailStore? thumbnails = null,
        IIndexerClient? indexer = null,
        ISettingsService? settings = null)
    {
        _detail = detail;
        _thumbnails = thumbnails;
        _indexer = indexer;
        _settings = settings;
        ContentPager = new PagedListState<ContentItemDto>(LoadContentPageAsync, defaultPageSize: 48);
        ContentPager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PagedListState<ContentItemDto>.IsLoading)
                or nameof(PagedListState<ContentItemDto>.TotalCount)
                or nameof(PagedListState<ContentItemDto>.SummaryLabel))
                NotifyChrome();
        };
    }

    public PagedListState<ContentItemDto> ContentPager { get; }
    public ObservableCollection<GalleryThumbViewModel> Thumbs { get; } = [];
    public ObservableCollection<CopyDto> Copies { get; } = [];

    /// <summary>Type filter chips: null/"All" plus ContentType names.</summary>
    public IReadOnlyList<string> TypeFilterOptions { get; } =
    [
        "All", "Scene", "Look", "Clothing", "Hairstyle", "Morph", "Pose", "Skin", "Plugin", "Asset",
    ];

    [ObservableProperty] private long _packageId;
    [ObservableProperty] private long? _selectedVarFileId;
    [ObservableProperty] private string _typeFilter = "All";
    [ObservableProperty] private bool _loadableOnly;
    [ObservableProperty] private PackageGalleryMode _mode = PackageGalleryMode.Gallery;
    [ObservableProperty] private GalleryThumbViewModel? _selectedThumb;
    [ObservableProperty] private Bitmap? _focusBitmap;
    [ObservableProperty] private bool _isFocusLoading;
    [ObservableProperty] private string? _focusStatus;
    [ObservableProperty] private bool _focusIsFallback;
    [ObservableProperty] private bool _isBinding;

    public bool IsGalleryMode => Mode == PackageGalleryMode.Gallery;
    public bool IsFocusMode => Mode == PackageGalleryMode.Focus;
    public bool HasPackage => PackageId > 0;
    public bool HasThumbs => Thumbs.Count > 0;
    public bool HasCopies => Copies.Count > 0;
    public bool IsEmpty => HasPackage && !IsBinding && !ContentPager.IsLoading && Thumbs.Count == 0;
    public bool IsContentLoading => IsBinding || ContentPager.IsLoading;
    public string ImageCountLabel => ContentPager.TotalCount == 0
        ? "0 items"
        : $"{ContentPager.SummaryLabel}";

    public string FocusCounter
    {
        get
        {
            var nav = PreviewEligibleThumbs().ToList();
            if (nav.Count == 0 || SelectedThumb is null)
                return "0 / 0";
            var index = nav.FindIndex(t => t.ContentItemId == SelectedThumb.ContentItemId);
            return index < 0 ? "— / " + nav.Count : $"{index + 1} / {nav.Count}";
        }
    }

    public string? FocusEntryPath => SelectedThumb?.EntryPath;
    public string? FocusType => SelectedThumb?.Type;
    public bool CanGoPrevious => PreviewEligibleIndex() > 0;
    public bool CanGoNext
    {
        get
        {
            var nav = PreviewEligibleThumbs().ToList();
            var i = PreviewEligibleIndex();
            return i >= 0 && i < nav.Count - 1;
        }
    }

    public async Task LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_settings is null)
            return;
        _suppressFilterPersist = true;
        var type = await _settings.GetAsync(PrefTypeFilter, cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(type) && TypeFilterOptions.Contains(type, StringComparer.OrdinalIgnoreCase))
            TypeFilter = type;
        LoadableOnly = await _settings.GetBoolAsync(PrefLoadableOnly, false, cancellationToken).ConfigureAwait(true);
        _suppressFilterPersist = false;
    }

    /// <summary>Clear UI immediately (stale-proof). Call before/when selection becomes null.</summary>
    public void Clear()
    {
        _bindCts?.Cancel();
        _focusCts?.Cancel();
        _bindGeneration++;
        _focusGeneration++;
        Mode = PackageGalleryMode.Gallery;
        ClearThumbs();
        ContentPager.Reset();
        Copies.Clear();
        PackageId = 0;
        SelectedVarFileId = null;
        SelectedThumb = null;
        FocusBitmap = null;
        FocusStatus = null;
        FocusIsFallback = false;
        IsFocusLoading = false;
        IsBinding = false;
        _focusCache.Clear();
        NotifyChrome();
    }

    /// <summary>
    /// Bind a package: clear first, then load copies + first content page. Always returns to Gallery
    /// mode (focus selection does not survive package changes).
    /// </summary>
    public async Task BindPackageAsync(
        long packageId,
        IReadOnlyList<CopyDto>? copies = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (packageId <= 0)
        {
            Clear();
            return;
        }

        _bindCts?.Cancel();
        _bindCts?.Dispose();
        _bindCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _bindCts.Token;
        var generation = ++_bindGeneration;

        // Clear stale previews immediately before any await.
        Mode = PackageGalleryMode.Gallery;
        ClearThumbs();
        ContentPager.Reset();
        SelectedThumb = null;
        FocusBitmap = null;
        FocusStatus = null;
        FocusIsFallback = false;
        IsFocusLoading = false;
        _focusCache.Clear();
        PackageId = packageId;
        IsBinding = true;
        NotifyChrome();

        try
        {
            Copies.Clear();
            IReadOnlyList<CopyDto> resolvedCopies = copies ?? [];
            if (resolvedCopies.Count == 0 && _detail is not null)
            {
                var detail = await _detail.GetAsync(packageId, token).ConfigureAwait(true);
                if (generation != _bindGeneration)
                    return;
                resolvedCopies = detail?.Copies ?? [];
            }

            if (generation != _bindGeneration)
                return;

            foreach (var copy in resolvedCopies)
                Copies.Add(copy);
            OnPropertyChanged(nameof(HasCopies));

            SelectedVarFileId = resolvedCopies.Count == 0
                ? null
                : resolvedCopies.FirstOrDefault(c => c.IsOnline)?.VarFileId
                  ?? resolvedCopies[0].VarFileId;

            await ReloadContentAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // superseded
        }
        finally
        {
            if (generation == _bindGeneration)
            {
                IsBinding = false;
                NotifyChrome();
            }
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await ContentPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        await ProjectThumbsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await ContentPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        await ProjectThumbsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GoToPageAsync(int page, CancellationToken cancellationToken = default)
    {
        await ContentPager.LoadPageAsync(page, ContentPager.PageSize, cancellationToken).ConfigureAwait(true);
        await ProjectThumbsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int size, CancellationToken cancellationToken = default)
    {
        await ContentPager.LoadPageAsync(1, size, cancellationToken).ConfigureAwait(true);
        await ProjectThumbsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task EnterFocusAsync(GalleryThumbViewModel? thumb, CancellationToken cancellationToken = default)
    {
        if (thumb is null || PackageId <= 0)
            return;
        SelectedThumb = thumb;
        foreach (var t in Thumbs)
            t.IsSelected = t.ContentItemId == thumb.ContentItemId;
        Mode = PackageGalleryMode.Focus;
        NotifyChrome();
        await LoadFocusAsync(thumb, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public void ExitFocus()
    {
        _focusCts?.Cancel();
        _focusGeneration++;
        IsFocusLoading = false;
        Mode = PackageGalleryMode.Gallery;
        // Keep SelectedThumb so returning to gallery shows selection accent.
        FocusBitmap = null;
        _focusCache.PinnedId = null;
        FocusStatus = null;
        FocusIsFallback = false;
        NotifyChrome();
    }

    [RelayCommand]
    public Task FocusFirstAsync(CancellationToken cancellationToken = default) =>
        NavigateFocusAsync(_ => 0, cancellationToken);

    [RelayCommand]
    public Task FocusPreviousAsync(CancellationToken cancellationToken = default) =>
        NavigateFocusAsync(i => Math.Max(0, i - 1), cancellationToken);

    [RelayCommand]
    public Task FocusNextAsync(CancellationToken cancellationToken = default) =>
        NavigateFocusAsync(i => i + 1, cancellationToken);

    [RelayCommand]
    public Task FocusLastAsync(CancellationToken cancellationToken = default) =>
        NavigateFocusAsync(i => PreviewEligibleThumbs().Count() - 1, cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _bindCts?.Cancel();
        _bindCts?.Dispose();
        _focusCts?.Cancel();
        _focusCts?.Dispose();
        ClearThumbs();
        _focusCache.Dispose();
        FocusBitmap = null;
    }

    partial void OnModeChanged(PackageGalleryMode value)
    {
        OnPropertyChanged(nameof(IsGalleryMode));
        OnPropertyChanged(nameof(IsFocusMode));
    }

    partial void OnTypeFilterChanged(string value)
    {
        if (_suppressFilterPersist || PackageId <= 0)
        {
            _ = PersistFiltersAsync();
            return;
        }
        ExitFocus();
        _ = ReloadAfterFilterAsync();
    }

    partial void OnLoadableOnlyChanged(bool value)
    {
        if (_suppressFilterPersist || PackageId <= 0)
        {
            _ = PersistFiltersAsync();
            return;
        }
        ExitFocus();
        _ = ReloadAfterFilterAsync();
    }

    partial void OnSelectedVarFileIdChanged(long? value)
    {
        if (PackageId <= 0 || IsBinding)
            return;
        ExitFocus();
        _ = ReloadAfterFilterAsync();
    }

    partial void OnSelectedThumbChanged(GalleryThumbViewModel? value)
    {
        OnPropertyChanged(nameof(FocusEntryPath));
        OnPropertyChanged(nameof(FocusType));
        OnPropertyChanged(nameof(FocusCounter));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private async Task ReloadAfterFilterAsync()
    {
        await PersistFiltersAsync().ConfigureAwait(true);
        await ReloadContentAsync().ConfigureAwait(true);
    }

    private async Task PersistFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (_settings is null || _suppressFilterPersist)
            return;
        await _settings.SetAsync(PrefTypeFilter, TypeFilter, cancellationToken).ConfigureAwait(true);
        await _settings.SetBoolAsync(PrefLoadableOnly, LoadableOnly, cancellationToken).ConfigureAwait(true);
    }

    private async Task ReloadContentAsync(CancellationToken cancellationToken = default)
    {
        if (_detail is null || PackageId <= 0)
            return;
        await ContentPager.LoadPageAsync(1, ContentPager.PageSize, cancellationToken).ConfigureAwait(true);
        await ProjectThumbsAsync().ConfigureAwait(true);
        NotifyChrome();
    }

    private Task<PageResult<ContentItemDto>> LoadContentPageAsync(PageRequest request, CancellationToken cancellationToken)
    {
        if (_detail is null || PackageId <= 0)
            return Task.FromResult(PageResult<ContentItemDto>.Empty(request));
        var type = string.Equals(TypeFilter, "All", StringComparison.OrdinalIgnoreCase) ? null : TypeFilter;
        return _detail.GetContentItemsPageAsync(PackageId, SelectedVarFileId, request, cancellationToken, type, LoadableOnly);
    }

    private async Task ProjectThumbsAsync()
    {
        ClearThumbs();
        if (PackageId <= 0)
            return;
        foreach (var item in ContentPager.Items)
            Thumbs.Add(new GalleryThumbViewModel(item, PackageId, _thumbnails, _indexer));
        NotifyChrome();
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private async Task NavigateFocusAsync(Func<int, int> indexSelector, CancellationToken cancellationToken)
    {
        var nav = PreviewEligibleThumbs().ToList();
        if (nav.Count == 0)
            return;
        var current = PreviewEligibleIndex();
        if (current < 0)
            current = 0;
        var next = Math.Clamp(indexSelector(current), 0, nav.Count - 1);
        await EnterFocusAsync(nav[next], cancellationToken).ConfigureAwait(true);
    }

    private IEnumerable<GalleryThumbViewModel> PreviewEligibleThumbs() =>
        Thumbs.Where(t => t.CanPreview);

    private int PreviewEligibleIndex()
    {
        if (SelectedThumb is null)
            return -1;
        var i = 0;
        foreach (var t in PreviewEligibleThumbs())
        {
            if (t.ContentItemId == SelectedThumb.ContentItemId)
                return i;
            i++;
        }
        return -1;
    }

    private async Task LoadFocusAsync(GalleryThumbViewModel thumb, CancellationToken cancellationToken)
    {
        _focusCts?.Cancel();
        _focusCts?.Dispose();
        _focusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _focusCts.Token;
        var generation = ++_focusGeneration;
        var contentId = thumb.ContentItemId;

        FocusBitmap = null;
        FocusIsFallback = false;
        FocusStatus = null;
        IsFocusLoading = true;
        _focusCache.PinnedId = null;
        NotifyChrome();

        try
        {
            if (_focusCache.TryGet(contentId, out var cached) && cached is not null)
            {
                if (generation != _focusGeneration)
                    return;
                _focusCache.PinnedId = contentId;
                FocusBitmap = cached;
                IsFocusLoading = false;
                NotifyChrome();
                return;
            }

            byte[]? bytes = null;
            if (_thumbnails is not null)
                bytes = await _thumbnails.GetFocusContentAsync(contentId, token).ConfigureAwait(false);

            if (bytes is null && thumb.CanPreview && _indexer is not null)
            {
                var extracted = await _indexer.ExtractContentFocusPreviewAsync(contentId, token).ConfigureAwait(false);
                if (extracted.IsSuccess && _thumbnails is not null)
                    bytes = await _thumbnails.GetFocusContentAsync(contentId, token).ConfigureAwait(false);
                else if (!extracted.IsSuccess)
                    FocusStatus = extracted.Error.Message;
            }

            // Fallback: wall thumb → package hero.
            if (bytes is null && _thumbnails is not null)
            {
                bytes = await _thumbnails.GetContentAsync(contentId, token).ConfigureAwait(false);
                if (bytes is not null)
                    FocusIsFallback = true;
            }
            if (bytes is null && _thumbnails is not null)
            {
                bytes = await _thumbnails.GetAsync(PackageId, token).ConfigureAwait(false);
                if (bytes is not null)
                    FocusIsFallback = true;
            }

            if (generation != _focusGeneration)
                return;

            if (bytes is null)
            {
                FocusStatus ??= thumb.CanPreview ? "Preview unavailable" : "No preview for this content type";
                return;
            }

            var bitmap = await Task.Run(() => new Bitmap(new MemoryStream(bytes)), token).ConfigureAwait(true);
            if (generation != _focusGeneration)
            {
                bitmap.Dispose();
                return;
            }

            _focusCache.PinnedId = contentId;
            _focusCache.Set(contentId, bitmap);
            FocusBitmap = bitmap;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // superseded
        }
        catch (Exception)
        {
            if (generation == _focusGeneration)
                FocusStatus = "Preview unavailable";
        }
        finally
        {
            if (generation == _focusGeneration)
            {
                IsFocusLoading = false;
                NotifyChrome();
            }
        }
    }

    private void ClearThumbs()
    {
        foreach (var thumb in Thumbs)
            thumb.Invalidate();
        Thumbs.Clear();
    }

    private void NotifyChrome()
    {
        OnPropertyChanged(nameof(HasPackage));
        OnPropertyChanged(nameof(HasThumbs));
        OnPropertyChanged(nameof(HasCopies));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsContentLoading));
        OnPropertyChanged(nameof(ImageCountLabel));
        OnPropertyChanged(nameof(FocusCounter));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(FocusEntryPath));
        OnPropertyChanged(nameof(FocusType));
        OnPropertyChanged(nameof(IsGalleryMode));
        OnPropertyChanged(nameof(IsFocusMode));
    }
}
