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
        PackageGallery = new PackageGalleryViewModel(detail, thumbnails, indexer);
        DirectDepsPager = new PagedListState<DependencyEdgeDto>((request, ct) => _detailQuery.GetDirectDependenciesPageAsync(PackageId, request, ct), defaultPageSize: 25);
        ReverseDepsPager = new PagedListState<ReverseDependentDto>((request, ct) => _detailQuery.GetReverseDependentsPageAsync(PackageId, request, ct), defaultPageSize: 25);
        SaveDepsPager = new PagedListState<DependencyEdgeDto>((request, ct) => _detailQuery.GetSaveDependentsPageAsync(PackageId, request, ct), defaultPageSize: 25);
        CopiesPager = new PagedListState<CopyDto>((request, ct) => _detailQuery.GetCopiesPageAsync(PackageId, request, ct), defaultPageSize: 25);
    }

    [ObservableProperty] private long _packageId;

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Dependencies"), new("Content gallery"), new("Copies & lineage")];
    [ObservableProperty] private int _selectedTabIndex;

    public IReadOnlyList<Controls.TabItemModel> DepsSections { get; } =
        [new("Direct"), new("Reverse"), new("Saves")];
    [ObservableProperty] private int _selectedDepsSectionIndex;

    public PackageGalleryViewModel PackageGallery { get; }
    public PagedListState<DependencyEdgeDto> DirectDepsPager { get; }
    public PagedListState<ReverseDependentDto> ReverseDepsPager { get; }
    public PagedListState<DependencyEdgeDto> SaveDepsPager { get; }
    public PagedListState<CopyDto> CopiesPager { get; }

    [ObservableProperty] private PackageDetail? _detail;

    public bool HasDetail => Detail is not null;

    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsDependenciesTab => SelectedTabIndex == 1;
    public bool IsContentTab => SelectedTabIndex == 2;
    public bool IsCopiesTab => SelectedTabIndex == 3;

    public bool IsDirectDepsSection => SelectedDepsSectionIndex == 0;
    public bool IsReverseDepsSection => SelectedDepsSectionIndex == 1;
    public bool IsSaveDepsSection => SelectedDepsSectionIndex == 2;

    public int ActiveDepsPageNumber => ActiveDepsPagerPageNumber();
    public int ActiveDepsPageCount => ActiveDepsPagerPageCount();
    public int ActiveDepsPageSize => ActiveDepsPagerPageSize();
    public int ActiveDepsTotalCount => ActiveDepsPagerTotalCount();
    public string ActiveDepsSummary => ActiveDepsPagerSummary();

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsDependenciesTab));
        OnPropertyChanged(nameof(IsContentTab));
        OnPropertyChanged(nameof(IsCopiesTab));
        _ = EnsureTabLoadedAsync();
    }

    partial void OnSelectedDepsSectionIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsDirectDepsSection));
        OnPropertyChanged(nameof(IsReverseDepsSection));
        OnPropertyChanged(nameof(IsSaveDepsSection));
        NotifyActiveDepsPager();
        _ = EnsureDepsSectionLoadedAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(long packageId, CancellationToken cancellationToken = default)
    {
        PackageId = packageId;
        DirectDepsPager.Reset();
        ReverseDepsPager.Reset();
        SaveDepsPager.Reset();
        PackageGallery.Clear();
        CopiesPager.Reset();
        Detail = await _detailQuery.GetAsync(packageId, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
        await EnsureTabLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private void EditMeta()
    {
        if (PackageId <= 0)
            return;
        _launcher?.OpenEditMeta(PackageId, onSaved: () => _ = LoadAsync(PackageId));
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

    [RelayCommand] private Task ActiveDepsPreviousPageAsync() => ActiveDepsNavigateAsync(-1);
    [RelayCommand] private Task ActiveDepsNextPageAsync() => ActiveDepsNavigateAsync(+1);
    [RelayCommand] private Task ActiveDepsGoToPageAsync(int page) => ActiveDepsLoadAsync(page, ActiveDepsPageSize);
    [RelayCommand] private Task ActiveDepsChangePageSizeAsync(int size) => ActiveDepsLoadAsync(1, size);

    [RelayCommand] private Task CopiesPreviousPageAsync() => CopiesPager.PreviousPageAsync();
    [RelayCommand] private Task CopiesNextPageAsync() => CopiesPager.NextPageAsync();
    [RelayCommand] private Task CopiesGoToPageAsync(int page) => CopiesPager.LoadPageAsync(page, CopiesPager.PageSize);
    [RelayCommand] private Task CopiesChangePageSizeAsync(int size) => CopiesPager.LoadPageAsync(1, size);

    private async Task ReloadDependenciesAsync()
    {
        await DirectDepsPager.ResetAndReloadAsync().ConfigureAwait(true);
        await SaveDepsPager.ResetAndReloadAsync().ConfigureAwait(true);
        Detail = await _detailQuery.GetAsync(PackageId).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
        NotifyActiveDepsPager();
    }

    private async Task EnsureTabLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (PackageId <= 0)
            return;
        if (IsDependenciesTab)
            await EnsureDepsSectionLoadedAsync(cancellationToken).ConfigureAwait(true);
        else if (IsContentTab && !PackageGallery.HasThumbs && !PackageGallery.IsBinding)
            await PackageGallery.BindPackageAsync(PackageId, Detail?.Copies, cancellationToken).ConfigureAwait(true);
        else if (IsCopiesTab && CopiesPager.Items.Count == 0 && !CopiesPager.IsLoading)
            await CopiesPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task EnsureDepsSectionLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (IsDirectDepsSection && DirectDepsPager.Items.Count == 0 && !DirectDepsPager.IsLoading)
            await DirectDepsPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        else if (IsReverseDepsSection && ReverseDepsPager.Items.Count == 0 && !ReverseDepsPager.IsLoading)
            await ReverseDepsPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        else if (IsSaveDepsSection && SaveDepsPager.Items.Count == 0 && !SaveDepsPager.IsLoading)
            await SaveDepsPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyActiveDepsPager();
    }

    private Task ActiveDepsNavigateAsync(int delta)
    {
        var page = Math.Max(1, ActiveDepsPageNumber + delta);
        return ActiveDepsLoadAsync(page, ActiveDepsPageSize);
    }

    private async Task ActiveDepsLoadAsync(int page, int pageSize)
    {
        if (IsDirectDepsSection)
            await DirectDepsPager.LoadPageAsync(page, pageSize).ConfigureAwait(true);
        else if (IsReverseDepsSection)
            await ReverseDepsPager.LoadPageAsync(page, pageSize).ConfigureAwait(true);
        else
            await SaveDepsPager.LoadPageAsync(page, pageSize).ConfigureAwait(true);
        NotifyActiveDepsPager();
    }

    private int ActiveDepsPagerPageNumber() => IsDirectDepsSection ? DirectDepsPager.PageNumber
        : IsReverseDepsSection ? ReverseDepsPager.PageNumber : SaveDepsPager.PageNumber;
    private int ActiveDepsPagerPageCount() => IsDirectDepsSection ? DirectDepsPager.PageCount
        : IsReverseDepsSection ? ReverseDepsPager.PageCount : SaveDepsPager.PageCount;
    private int ActiveDepsPagerPageSize() => IsDirectDepsSection ? DirectDepsPager.PageSize
        : IsReverseDepsSection ? ReverseDepsPager.PageSize : SaveDepsPager.PageSize;
    private int ActiveDepsPagerTotalCount() => IsDirectDepsSection ? DirectDepsPager.TotalCount
        : IsReverseDepsSection ? ReverseDepsPager.TotalCount : SaveDepsPager.TotalCount;
    private string ActiveDepsPagerSummary() => IsDirectDepsSection ? DirectDepsPager.SummaryLabel
        : IsReverseDepsSection ? ReverseDepsPager.SummaryLabel : SaveDepsPager.SummaryLabel;

    private void NotifyActiveDepsPager()
    {
        OnPropertyChanged(nameof(ActiveDepsPageNumber));
        OnPropertyChanged(nameof(ActiveDepsPageCount));
        OnPropertyChanged(nameof(ActiveDepsPageSize));
        OnPropertyChanged(nameof(ActiveDepsTotalCount));
        OnPropertyChanged(nameof(ActiveDepsSummary));
    }
}
