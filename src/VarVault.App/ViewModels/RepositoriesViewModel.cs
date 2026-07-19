using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>SCR-3 · Repositories: cards per registered repository. (16-checklist SCR-3.)</summary>
public sealed partial class RepositoriesViewModel(IRepositoryService repositories) : ObservableObject
{
    public ObservableCollection<RepositoryInfo> Repositories { get; } = [];

    public bool IsEmpty => Repositories.Count == 0;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Repositories.Clear();
        foreach (var r in await repositories.ListAsync(cancellationToken).ConfigureAwait(true))
            Repositories.Add(r);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public async Task BenchmarkAsync(RepositoryInfo repo, CancellationToken cancellationToken = default)
    {
        var updated = await repositories.BenchmarkAsync(repo.Id, cancellationToken).ConfigureAwait(true);
        if (updated is not null)
            await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
