using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;

namespace VarVault.App.ViewModels;

/// <summary>Activity history screen: recent audited actions, newest first. (Checklist X.12.)</summary>
public sealed partial class ActivityViewModel(IActivityLog log) : ObservableObject
{
    public ObservableCollection<ActivityRecord> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        foreach (var record in await log.GetRecentAsync(cancellationToken: cancellationToken).ConfigureAwait(true))
            Items.Add(record);
        OnPropertyChanged(nameof(IsEmpty));
    }
}
