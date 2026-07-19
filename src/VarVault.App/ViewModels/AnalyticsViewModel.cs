using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>A space-by-group row with a 0..1 bar fraction (relative to the group max) + a size label. (GD-14)</summary>
public sealed class SpaceRowViewModel(string group, long bytes, long max)
{
    public string Group => group;
    public long TotalBytes => bytes;
    public double Fraction => max > 0 ? Math.Clamp(bytes / (double)max, 0, 1) : 0;
    public string SizeLabel => bytes >= 1L << 40
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 40):F1} TB")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F0} GB");
}

/// <summary>Analytics screen: where space goes, by creator/type/tier, with bars. (Checklist 5.16 / GD-14.)</summary>
public sealed partial class AnalyticsViewModel(IAnalyticsService analytics) : ObservableObject
{
    public ObservableCollection<SpaceRowViewModel> ByCreator { get; } = [];
    public ObservableCollection<SpaceRowViewModel> ByType { get; } = [];
    public ObservableCollection<SpaceRowViewModel> ByTier { get; } = [];

    /// <summary>Cold-on-fast-storage waste (GB), for the "wasting fast storage" card. (GD-14)</summary>
    [ObservableProperty] private long _wastedOnFastBytes;

    /// <summary>Explicit empty-state flag. (GF-2)</summary>
    public bool IsEmpty => ByType.Count == 0 && ByCreator.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await FillAsync(ByCreator, analytics.SpaceByCreatorAsync(cancellationToken)).ConfigureAwait(true);
        await FillAsync(ByType, analytics.SpaceByTypeAsync(cancellationToken)).ConfigureAwait(true);
        await FillAsync(ByTier, analytics.SpaceByTierAsync(cancellationToken)).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static async Task FillAsync(ObservableCollection<SpaceRowViewModel> target, Task<IReadOnlyList<SpaceByGroup>> source)
    {
        var rows = await source.ConfigureAwait(true);
        var max = rows.Count == 0 ? 0 : rows.Max(r => r.TotalBytes);
        target.Clear();
        foreach (var row in rows)
            target.Add(new SpaceRowViewModel(row.Group, row.TotalBytes, max));
    }
}
