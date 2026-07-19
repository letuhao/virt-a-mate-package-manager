using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;

namespace VarVault.App.ViewModels;

/// <summary>Activity history screen: recent audited actions, newest first. (Checklist X.12.)</summary>
public sealed partial class ActivityViewModel(IActivityLog log) : ObservableObject
{
    private readonly List<ActivityRecord> _all = [];

    public ObservableCollection<ActivityRecord> Items { get; } = [];

    /// <summary>Action filter options (prototype dropdown). (GD-16)</summary>
    public IReadOnlyList<string> Filters { get; } = ["All actions", "Migrations", "Deletes", "Fixes"];
    [ObservableProperty] private string _selectedFilter = "All actions";

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _all.Clear();
        foreach (var record in await log.GetRecentAsync(cancellationToken: cancellationToken).ConfigureAwait(true))
            _all.Add(record);
        Apply();
    }

    partial void OnSelectedFilterChanged(string value) => Apply();

    private void Apply()
    {
        Items.Clear();
        foreach (var r in _all.Where(Matches))
            Items.Add(r);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(ActivityRecord r) => SelectedFilter switch
    {
        "Migrations" => r.Kind.Contains("migrat", StringComparison.OrdinalIgnoreCase),
        "Deletes" => r.Kind.Contains("delet", StringComparison.OrdinalIgnoreCase) || r.Kind.Contains("trash", StringComparison.OrdinalIgnoreCase),
        "Fixes" => r.Kind.Contains("fix", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };
}
