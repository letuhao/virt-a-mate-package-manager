using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-1 · Dashboard: summary tiles from <see cref="IDashboardService"/>. (16-checklist SCR-1.)</summary>
public sealed partial class DashboardViewModel(IDashboardService dashboard) : ObservableObject
{
    [ObservableProperty] private DashboardSummary? _summary;

    public bool HasSummary => Summary is not null;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Summary = await dashboard.GetSummaryAsync(cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasSummary));
    }
}
