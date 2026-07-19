using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts. (Checklist 2.14.)</summary>
public sealed partial class MissingDepsViewModel(IMissingDepsQuery query) : ObservableObject
{
    public ObservableCollection<MissingDependency> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        foreach (var item in await query.GetMissingAsync(cancellationToken).ConfigureAwait(true))
            Items.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
