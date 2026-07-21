using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-5 · Tiering &amp; migration: class counts + misplaced proposals. (16-checklist SCR-5.)</summary>
public sealed partial class TieringViewModel(
    ITieringService tiering, Services.IDialogLauncher? launcher = null,
    Sdk.Repositories.IRepositoryService? repositories = null) : ObservableObject, ILoadableScreen
{
    public PagedListState<MisplacedItem> MisplacedPager { get; } =
        new((request, ct) => tiering.MisplacedPageAsync(request, ct));
    public PagedListState<StaleVersion> StalePager { get; } =
        new((request, ct) => tiering.StaleVersionsPageAsync(request, ct));

    /// <summary>True unless every registered repository is cold (T3) — i.e. there is at least one T1/T2 fast drive.
    /// Default true so the HDD-only hint stays hidden until Load proves otherwise. (28-checklist E1.)</summary>
    [ObservableProperty] private bool _hasFastDrive = true;

    /// <summary>Show the "all storage is cold" hint: repositories exist but none is tiered hot/warm. (E1.)</summary>
    public bool ShowHddOnlyHint => !HasFastDrive;

    partial void OnHasFastDriveChanged(bool value) => OnPropertyChanged(nameof(ShowHddOnlyHint));

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

    public ObservableCollection<MisplacedItem> Misplaced => MisplacedPager.Items;

    /// <summary>Active class→tier placement policy for the Placement/Lifecycle tabs. (24-checklist A6)</summary>
    public ObservableCollection<TierPolicyEntry> Policy { get; } = [];
    /// <summary>Superseded cold versions for the Stale tab. (24-checklist A7)</summary>
    public ObservableCollection<StaleVersion> StaleVersions => StalePager.Items;
    public bool StaleIsEmpty => StalePager.IsEmpty;

    /// <summary>Explicit empty-state flag for the misplaced list. (GF-2)</summary>
    public bool IsEmpty => MisplacedPager.IsEmpty;

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
        await MisplacedPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsEmpty));

        Policy.Clear();
        foreach (var p in (await tiering.PolicyAsync(cancellationToken).ConfigureAwait(true)).Placements)
            Policy.Add(p);
        await StalePager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(StaleIsEmpty));

        // E1 · detect HDD-only setups (no T1/T2 drive) so the screen can explain why nothing is hot/warm.
        if (repositories is not null)
        {
            var repos = await repositories.ListAsync(cancellationToken).ConfigureAwait(true);
            HasFastDrive = repos.Count == 0 || repos.Any(r => r.Tier <= 2);
        }
    }

    [RelayCommand]
    public async Task BuildPlanAsync(CancellationToken cancellationToken = default)
    {
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(true);
        PlannedMoves = plan.Proposals.Count;
    }

    [RelayCommand(CanExecute = nameof(CanMisplacedPreviousPage))]
    private async Task MisplacedPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await MisplacedPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanMisplacedNextPage))]
    private async Task MisplacedNextPageAsync(CancellationToken cancellationToken = default)
    {
        await MisplacedPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task MisplacedGoToPageAsync(int pageNumber)
    {
        await MisplacedPager.LoadPageAsync(pageNumber, MisplacedPager.PageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task MisplacedChangePageSizeAsync(int pageSize)
    {
        await MisplacedPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanStalePreviousPage))]
    private async Task StalePreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await StalePager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanStaleNextPage))]
    private async Task StaleNextPageAsync(CancellationToken cancellationToken = default)
    {
        await StalePager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task StaleGoToPageAsync(int pageNumber)
    {
        await StalePager.LoadPageAsync(pageNumber, StalePager.PageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task StaleChangePageSizeAsync(int pageSize)
    {
        await StalePager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    private bool CanMisplacedPreviousPage() => MisplacedPager.HasPreviousPage && !MisplacedPager.IsLoading;
    private bool CanMisplacedNextPage() => MisplacedPager.HasNextPage && !MisplacedPager.IsLoading;
    private bool CanStalePreviousPage() => StalePager.HasPreviousPage && !StalePager.IsLoading;
    private bool CanStaleNextPage() => StalePager.HasNextPage && !StalePager.IsLoading;

    private void NotifyPaged()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(StaleIsEmpty));
        MisplacedPreviousPageCommand.NotifyCanExecuteChanged();
        MisplacedNextPageCommand.NotifyCanExecuteChanged();
        StalePreviousPageCommand.NotifyCanExecuteChanged();
        StaleNextPageCommand.NotifyCanExecuteChanged();
    }
}
