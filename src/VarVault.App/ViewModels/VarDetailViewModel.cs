using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-10 · Var detail: identity, deps closure, content items, copies/lineage. (16-checklist DLG-10.)</summary>
public sealed partial class VarDetailViewModel(IPackageDetailQuery detail) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Dependency graph"), new("Content items"), new("Copies & lineage")];
    [ObservableProperty] private int _selectedTabIndex;

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
    }

    [RelayCommand]
    public async Task LoadAsync(long packageId, CancellationToken cancellationToken = default)
    {
        Detail = await detail.GetAsync(packageId, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
    }
}
