using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-5 · Tiering &amp; migration: class counts + misplaced proposals. (16-checklist SCR-5.)</summary>
public sealed partial class TieringViewModel(ITieringService tiering) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2); per-tab content lands with the screen items.</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Lifecycle rules"), new("Placement policy"), new("Stale / old versions")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<MisplacedItem> Misplaced { get; } = [];

    [ObservableProperty] private TierClassCounts? _counts;
    [ObservableProperty] private int _plannedMoves;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Counts = await tiering.ClassCountsAsync(cancellationToken).ConfigureAwait(true);
        Misplaced.Clear();
        foreach (var m in await tiering.MisplacedAsync(cancellationToken).ConfigureAwait(true))
            Misplaced.Add(m);
    }

    [RelayCommand]
    public async Task BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(true);
        PlannedMoves = plan.Proposals.Count;
    }
}
