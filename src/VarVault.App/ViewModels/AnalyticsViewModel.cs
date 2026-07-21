using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>A space-by-group row with a 0..1 bar fraction (relative to the group max) + a size label. (GD-14)</summary>
public sealed class SpaceRowViewModel(string group, long bytes, long max)
{
    public string Group => group;
    public long TotalBytes => bytes;
    public double Fraction => max > 0 ? Math.Clamp(bytes / (double)max, 0, 1) : 0;
    public string SizeLabel => Common.Formatting.ByteSize.Humanize(bytes);
}

/// <summary>Analytics screen: where space goes, by creator/type/tier, with bars. (Checklist 5.16 / GD-14.)</summary>
public sealed partial class AnalyticsViewModel(IAnalyticsService analytics, ITieringService? tiering = null) : ObservableObject, ILoadableScreen
{
    public PagedListState<SpaceRowViewModel> CreatorPager { get; } =
        new(async (request, ct) =>
        {
            var page = await analytics.SpaceByCreatorPageAsync(request, ct).ConfigureAwait(false);
            var max = page.Items.Count == 0 ? 0 : page.Items.Max(r => r.TotalBytes);
            return new PageResult<SpaceRowViewModel>(
                page.Items.Select(r => new SpaceRowViewModel(r.Group, r.TotalBytes, max)).ToList(),
                page.TotalCount,
                page.PageNumber,
                page.PageSize);
        });

    public ObservableCollection<SpaceRowViewModel> ByCreator => CreatorPager.Items;
    public ObservableCollection<SpaceRowViewModel> ByType { get; } = [];
    public ObservableCollection<SpaceRowViewModel> ByTier { get; } = [];

    /// <summary>Cold-on-fast-storage waste (GB), for the "wasting fast storage" card. (GD-14)</summary>
    [ObservableProperty] private long _wastedOnFastBytes;

    /// <summary>Explicit empty-state flag. (GF-2)</summary>
    public bool IsEmpty => ByType.Count == 0 && ByCreator.Count == 0;

    /// <summary>Per-bar pixel heights for the "usage over time" sparkline card (space profile). (AC-18)</summary>
    public ObservableCollection<double> SparkBars { get; } = [];

    /// <summary>ILoadableScreen: the shell loads this screen by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await CreatorPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await FillAsync(ByType, analytics.SpaceByTypeAsync(cancellationToken)).ConfigureAwait(true);
        await FillAsync(ByTier, analytics.SpaceByTierAsync(cancellationToken)).ConfigureAwait(true);
        SparkBars.Clear();
        foreach (var r in ByType)
            SparkBars.Add(4 + r.Fraction * 30); // 4..34 px tall

        // G-4.2 · "wasting fast storage" = bytes placed on a faster tier than the class wants (cold-on-SSD, etc.).
        if (tiering is not null)
        {
            var misplaced = await tiering.MisplacedAsync(cancellationToken).ConfigureAwait(true);
            WastedOnFastBytes = misplaced.Where(m => m.CurrentTier < m.DesiredTier).Sum(m => m.SizeBytes);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand(CanExecute = nameof(CanCreatorPreviousPage))]
    private async Task CreatorPreviousPageAsync(CancellationToken cancellationToken = default) =>
        await CreatorPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanCreatorNextPage))]
    private async Task CreatorNextPageAsync(CancellationToken cancellationToken = default) =>
        await CreatorPager.NextPageAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand]
    private async Task CreatorGoToPageAsync(int pageNumber) =>
        await CreatorPager.LoadPageAsync(pageNumber, CreatorPager.PageSize).ConfigureAwait(true);

    [RelayCommand]
    private async Task CreatorChangePageSizeAsync(int pageSize) =>
        await CreatorPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);

    private bool CanCreatorPreviousPage() => CreatorPager.HasPreviousPage && !CreatorPager.IsLoading;
    private bool CanCreatorNextPage() => CreatorPager.HasNextPage && !CreatorPager.IsLoading;

    private static async Task FillAsync(ObservableCollection<SpaceRowViewModel> target, Task<IReadOnlyList<SpaceByGroup>> source)
    {
        var rows = await source.ConfigureAwait(true);
        var max = rows.Count == 0 ? 0 : rows.Max(r => r.TotalBytes);
        target.Clear();
        foreach (var row in rows)
            target.Add(new SpaceRowViewModel(row.Group, row.TotalBytes, max));
    }
}
