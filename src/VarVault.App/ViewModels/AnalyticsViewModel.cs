using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Analytics screen: where space goes, by creator/type/tier. (Checklist 5.16.)</summary>
public sealed partial class AnalyticsViewModel(IAnalyticsService analytics) : ObservableObject
{
    public ObservableCollection<SpaceByGroup> ByCreator { get; } = [];
    public ObservableCollection<SpaceByGroup> ByType { get; } = [];
    public ObservableCollection<SpaceByGroup> ByTier { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await FillAsync(ByCreator, analytics.SpaceByCreatorAsync(cancellationToken)).ConfigureAwait(true);
        await FillAsync(ByType, analytics.SpaceByTypeAsync(cancellationToken)).ConfigureAwait(true);
        await FillAsync(ByTier, analytics.SpaceByTierAsync(cancellationToken)).ConfigureAwait(true);
    }

    private static async Task FillAsync(ObservableCollection<SpaceByGroup> target, Task<IReadOnlyList<SpaceByGroup>> source)
    {
        target.Clear();
        foreach (var row in await source.ConfigureAwait(true))
            target.Add(row);
    }
}
