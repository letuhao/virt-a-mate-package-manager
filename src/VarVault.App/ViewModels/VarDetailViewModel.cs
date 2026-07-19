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

    [RelayCommand]
    public async Task LoadAsync(long packageId, CancellationToken cancellationToken = default)
    {
        Detail = await detail.GetAsync(packageId, cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasDetail));
    }
}
