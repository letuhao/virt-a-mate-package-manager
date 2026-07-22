using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-10 · Var detail: identity, deps, content gallery, copies/lineage. (16-checklist DLG-10.)</summary>
public sealed partial class VarDetailViewModel : ObservableObject
{
    private readonly IPackageDetailQuery _detailQuery;
    private readonly IDialogLauncher? _launcher;
    private readonly IClipboard? _clipboard;
    private readonly ThumbnailLoader<Avalonia.Media.Imaging.Bitmap>? _thumbnailLoader;

    public VarDetailViewModel(
        IPackageDetailQuery detail,
        IDialogLauncher? launcher = null,
        IClipboard? clipboard = null,
        IThumbnailStore? thumbnails = null,
        IIndexerClient? indexer = null)
    {
        _detailQuery = detail;
        _launcher = launcher;
        _clipboard = clipboard;
        _thumbnailLoader = thumbnails is null ? null : ThumbnailLoader.ForBitmap(thumbnails);
        PackageGallery = new PackageGalleryViewModel(detail, thumbnails, indexer);
        DirectDepsPager = new PagedListState<DependencyEdgeDto>((request, ct) => _detailQuery.GetDirectDependenciesPageAsync(PackageId, request, ct));
        ReverseDepsPager = new PagedListState<ReverseDependentDto>((request, ct) => _detailQuery.GetReverseDependentsPageAsync(PackageId, request, ct));
        SaveDepsPager = new PagedListState<DependencyEdgeDto>((request, ct) => _detailQuery.GetSaveDependentsPageAsync(PackageId, request, ct));
        CopiesPager = new PagedListState<CopyDto>((request, ct) => _detailQuery.GetCopiesPageAsync(PackageId, request, ct));
    }

    [ObservableProperty] private long _packageId;

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Dependencies"), new("Content gallery"), new("Copies & lineage")];
    [ObservableProperty] private int _selectedTabIndex;

    public PackageGalleryViewModel PackageGallery { get; }
    public PagedListState<DependencyEdgeDto> DirectDepsPager { get; }
    public PagedListState<ReverseDependentDto> ReverseDepsPager { get; }
    public PagedListState<DependencyEdgeDto> SaveDepsPager { get; }
    public PagedListState<CopyDto> CopiesPager { get; }
    public ObservableCollection<DependencyCardViewModel> DirectDependencyCards { get; } = [];

    [ObservableProperty] private PackageDetail? _detail;

    public bool HasDetail => Detail is not null;

    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsDependenciesTab => SelectedTabIndex == 1;
    public bool IsContentTab => SelectedTabIndex == 2;
    public bool IsCopiesTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsDependenciesTab));
        OnPropertyChanged(nameof(IsContentTab));
        OnPropertyChanged(nameof(IsCopiesTab));
        _ = EnsureTabLoadedAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(long packageId, CancellationToken cancellationToken = default)
    {
        PackageId = packageId;
        DirectDepsPager.Reset();
        DirectDependencyCards.Clear();
        ReverseDepsPager.Reset();
        SaveDepsPager.Reset();
        PackageGallery.Clear();
        CopiesPager.Reset();
        Detail = await _detailQuery.GetAsync(packageId, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
        await EnsureTabLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenDependency(DependencyEdgeDto edge)
    {
        if (edge.ResolvedPackage is { } card)
            _launcher?.OpenVarDetail(card.PackageId, push: true);
    }

    [RelayCommand]
    private async Task CopyRefAsync(DependencyEdgeDto edge, CancellationToken cancellationToken = default)
    {
        if (_clipboard is not null)
            await _clipboard.SetTextAsync(edge.RequestedRefRaw, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ResolveAlias(DependencyEdgeDto edge) =>
        _launcher?.OpenAlias(edge.RequestedRefRaw, onSaved: () => _ = ReloadDependenciesAsync());

    // ── Direct deps pager ─────────────────────────────────────────────────────
    [RelayCommand] private Task DirectDepsPreviousPageAsync() => LoadDirectDependenciesPageAsync(Math.Max(1, DirectDepsPager.PageNumber - 1), DirectDepsPager.PageSize);
    [RelayCommand] private Task DirectDepsNextPageAsync() => LoadDirectDependenciesPageAsync(DirectDepsPager.PageNumber + 1, DirectDepsPager.PageSize);
    [RelayCommand] private Task DirectDepsGoToPageAsync(int page) => LoadDirectDependenciesPageAsync(page, DirectDepsPager.PageSize);
    [RelayCommand] private Task DirectDepsChangePageSizeAsync(int size) => LoadDirectDependenciesPageAsync(1, size);

    // ── Reverse deps pager ────────────────────────────────────────────────────
    [RelayCommand] private Task ReverseDepsPreviousPageAsync() => ReverseDepsPager.PreviousPageAsync();
    [RelayCommand] private Task ReverseDepsNextPageAsync() => ReverseDepsPager.NextPageAsync();
    [RelayCommand] private Task ReverseDepsGoToPageAsync(int page) => ReverseDepsPager.LoadPageAsync(page, ReverseDepsPager.PageSize);
    [RelayCommand] private Task ReverseDepsChangePageSizeAsync(int size) => ReverseDepsPager.LoadPageAsync(1, size);

    // ── Save refs pager ───────────────────────────────────────────────────────
    [RelayCommand] private Task SaveDepsPreviousPageAsync() => SaveDepsPager.PreviousPageAsync();
    [RelayCommand] private Task SaveDepsNextPageAsync() => SaveDepsPager.NextPageAsync();
    [RelayCommand] private Task SaveDepsGoToPageAsync(int page) => SaveDepsPager.LoadPageAsync(page, SaveDepsPager.PageSize);
    [RelayCommand] private Task SaveDepsChangePageSizeAsync(int size) => SaveDepsPager.LoadPageAsync(1, size);

    // ── Copies pager ──────────────────────────────────────────────────────────
    [RelayCommand] private Task CopiesPreviousPageAsync() => CopiesPager.PreviousPageAsync();
    [RelayCommand] private Task CopiesNextPageAsync() => CopiesPager.NextPageAsync();
    [RelayCommand] private Task CopiesGoToPageAsync(int page) => CopiesPager.LoadPageAsync(page, CopiesPager.PageSize);
    [RelayCommand] private Task CopiesChangePageSizeAsync(int size) => CopiesPager.LoadPageAsync(1, size);

    private async Task ReloadDependenciesAsync()
    {
        await LoadDirectDependenciesPageAsync(1, DirectDepsPager.PageSize).ConfigureAwait(true);
        await SaveDepsPager.ResetAndReloadAsync().ConfigureAwait(true);
        Detail = await _detailQuery.GetAsync(PackageId).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
    }

    private async Task LoadDirectDependenciesPageAsync(
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        await DirectDepsPager.LoadPageAsync(pageNumber, pageSize, cancellationToken).ConfigureAwait(true);
        DirectDependencyCards.Clear();
        foreach (var edge in DirectDepsPager.Items)
            DirectDependencyCards.Add(new DependencyCardViewModel(edge, _thumbnailLoader));
    }

    private async Task EnsureTabLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (PackageId <= 0)
            return;
        if (IsDependenciesTab)
        {
            if (DirectDepsPager.Items.Count == 0 && !DirectDepsPager.IsLoading)
                await LoadDirectDependenciesPageAsync(1, DirectDepsPager.PageSize, cancellationToken).ConfigureAwait(true);
            if (ReverseDepsPager.Items.Count == 0 && !ReverseDepsPager.IsLoading)
                await ReverseDepsPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
            if (SaveDepsPager.Items.Count == 0 && !SaveDepsPager.IsLoading)
                await SaveDepsPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        }
        else if (IsContentTab && !PackageGallery.HasThumbs && !PackageGallery.IsBinding)
            await PackageGallery.BindPackageAsync(PackageId, Detail?.Copies, cancellationToken).ConfigureAwait(true);
        else if (IsCopiesTab && CopiesPager.Items.Count == 0 && !CopiesPager.IsLoading)
            await CopiesPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
    }
}
