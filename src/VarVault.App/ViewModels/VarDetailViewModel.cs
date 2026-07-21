using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-10 · Var detail: identity, deps closure, content items, copies/lineage. (16-checklist DLG-10.)</summary>
public sealed partial class VarDetailViewModel : ObservableObject
{
    private readonly IPackageDetailQuery _detailQuery;

    public VarDetailViewModel(IPackageDetailQuery detail)
    {
        _detailQuery = detail;
        GraphPager = new PagedListState<long>((request, ct) => _detailQuery.GetForwardClosurePageAsync(PackageId, request, ct));
        ContentPager = new PagedListState<ContentItemDto>((request, ct) => _detailQuery.GetContentItemsPageAsync(PackageId, request, ct));
        CopiesPager = new PagedListState<CopyDto>((request, ct) => _detailQuery.GetCopiesPageAsync(PackageId, request, ct));
    }

    [ObservableProperty] private long _packageId;

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Dependency graph"), new("Content items"), new("Copies & lineage")];
    [ObservableProperty] private int _selectedTabIndex;

    public PagedListState<long> GraphPager { get; }
    public PagedListState<ContentItemDto> ContentPager { get; }
    public PagedListState<CopyDto> CopiesPager { get; }

    [ObservableProperty] private PackageDetail? _detail;

    public bool HasDetail => Detail is not null;

    // Per-tab visibility so the tab strip actually switches content. (AC-24)
    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsGraphTab => SelectedTabIndex == 1;
    public bool IsContentTab => SelectedTabIndex == 2;
    public bool IsCopiesTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsGraphTab));
        OnPropertyChanged(nameof(IsContentTab));
        OnPropertyChanged(nameof(IsCopiesTab));
        _ = EnsureTabLoadedAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(long packageId, CancellationToken cancellationToken = default)
    {
        PackageId = packageId;
        Detail = await _detailQuery.GetAsync(packageId, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
        await EnsureTabLoadedAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task EnsureTabLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (PackageId <= 0)
            return;
        if (IsGraphTab && GraphPager.Items.Count == 0 && !GraphPager.IsLoading)
            await GraphPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        else if (IsContentTab && ContentPager.Items.Count == 0 && !ContentPager.IsLoading)
            await ContentPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        else if (IsCopiesTab && CopiesPager.Items.Count == 0 && !CopiesPager.IsLoading)
            await CopiesPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
    }
}
