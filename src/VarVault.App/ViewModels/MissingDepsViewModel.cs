using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Missing-deps screen: unresolved refs with needed-by counts. (Checklist 2.14.)</summary>
public sealed partial class MissingDepsViewModel(
    IMissingDepsQuery query, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    public ObservableCollection<MissingDependency> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Resolve/Edit-alias → alias dialog for the missing ref. (GD-12)</summary>
    [RelayCommand]
    private void Resolve(MissingDependency dep)
    {
        if (dep is not null)
            launcher?.OpenAlias(dep.Ref);
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        foreach (var item in await query.GetMissingAsync(cancellationToken).ConfigureAwait(true))
            Items.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>The most recent export text (missing refs, one per line) for save-to-file. (AC-17)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Screen-head "Export links txt": export the missing refs as a txt list. (AC-17)</summary>
    [RelayCommand]
    public void ExportLinks() =>
        LastExportText = string.Join(System.Environment.NewLine, Items.Select(i => i.Ref));
}
