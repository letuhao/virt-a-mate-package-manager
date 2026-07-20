using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-5 · Tiering &amp; migration: class counts + misplaced proposals. (16-checklist SCR-5.)</summary>
public sealed partial class TieringViewModel(
    ITieringService tiering, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    /// <summary>Sub-navigation tabs (GC-2); per-tab content lands with the screen items.</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Overview"), new("Lifecycle rules"), new("Placement policy"), new("Stale / old versions")];
    [ObservableProperty] private int _selectedTabIndex;

    // Per-tab visibility so switching a tab actually swaps content. (24-checklist A1/A5-A7)
    public bool IsOverviewTab => SelectedTabIndex == 0;
    public bool IsLifecycleTab => SelectedTabIndex == 1;
    public bool IsPlacementTab => SelectedTabIndex == 2;
    public bool IsStaleTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewTab));
        OnPropertyChanged(nameof(IsLifecycleTab));
        OnPropertyChanged(nameof(IsPlacementTab));
        OnPropertyChanged(nameof(IsStaleTab));
    }

    /// <summary>Per-row "Plan…" / screen-head "Review migration plan" → migrate dialog. (GD-9)</summary>
    [RelayCommand] private void Plan() => launcher?.OpenMigratePlan();

    /// <summary>Screen-head "Simulate policy…" → propose-only plan (BE-G5), shown via migrate dialog. (GD-9)</summary>
    [RelayCommand] private void Simulate() => launcher?.OpenMigratePlan();

    public ObservableCollection<MisplacedItem> Misplaced { get; } = [];

    /// <summary>Active class→tier placement policy for the Placement/Lifecycle tabs. (24-checklist A6)</summary>
    public ObservableCollection<TierPolicyEntry> Policy { get; } = [];
    /// <summary>Superseded cold versions for the Stale tab. (24-checklist A7)</summary>
    public ObservableCollection<StaleVersion> StaleVersions { get; } = [];
    public bool StaleIsEmpty => StaleVersions.Count == 0;

    /// <summary>Explicit empty-state flag for the misplaced list. (GF-2)</summary>
    public bool IsEmpty => Misplaced.Count == 0;

    [ObservableProperty] private TierClassCounts? _counts;
    [ObservableProperty] private int _plannedMoves;

    // Class-card bar fractions (0..1 of the largest class), so each card shows a proportional bar. (AC-14)
    private int MaxClass => Counts is null ? 0 : Math.Max(1, Math.Max(Counts.Hot, Math.Max(Counts.Warm, Counts.Cold)));
    public double HotFraction => Counts is null ? 0 : Counts.Hot / (double)MaxClass;
    public double WarmFraction => Counts is null ? 0 : Counts.Warm / (double)MaxClass;
    public double ColdFraction => Counts is null ? 0 : Counts.Cold / (double)MaxClass;

    partial void OnCountsChanged(TierClassCounts? value)
    {
        OnPropertyChanged(nameof(HotFraction));
        OnPropertyChanged(nameof(WarmFraction));
        OnPropertyChanged(nameof(ColdFraction));
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Counts = await tiering.ClassCountsAsync(cancellationToken).ConfigureAwait(true);
        Misplaced.Clear();
        foreach (var m in await tiering.MisplacedAsync(cancellationToken).ConfigureAwait(true))
            Misplaced.Add(m);
        OnPropertyChanged(nameof(IsEmpty));

        Policy.Clear();
        foreach (var p in (await tiering.PolicyAsync(cancellationToken).ConfigureAwait(true)).Placements)
            Policy.Add(p);
        StaleVersions.Clear();
        foreach (var s in await tiering.StaleVersionsAsync(cancellationToken).ConfigureAwait(true))
            StaleVersions.Add(s);
        OnPropertyChanged(nameof(StaleIsEmpty));
    }

    [RelayCommand]
    public async Task BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(true);
        PlannedMoves = plan.Proposals.Count;
    }
}
