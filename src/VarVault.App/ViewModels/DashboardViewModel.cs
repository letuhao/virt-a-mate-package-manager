using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-1 · Dashboard: summary tiles from <see cref="IDashboardService"/>. (16-checklist SCR-1.)</summary>
public sealed partial class DashboardViewModel(
    IDashboardService dashboard, Services.IDialogLauncher? launcher = null,
    IReclaimService? reclaim = null, IActivityLog? activity = null) : ObservableObject
{
    [ObservableProperty] private DashboardSummary? _summary;

    public bool HasSummary => Summary is not null;

    /// <summary>Reclaimable bytes headline — sum of redundant duplicate copies. (24-checklist E6)</summary>
    [ObservableProperty] private long _reclaimableBytes;

    /// <summary>Recent audited actions for the dashboard's activity card. (24-checklist E7)</summary>
    public ObservableCollection<ActivityRecord> RecentActivity { get; } = [];
    public bool HasRecentActivity => RecentActivity.Count > 0;

    // Classification stacked-bar fractions (0..1 of the classified total). (24-checklist E8)
    private int ClassTotal => Summary is null ? 0 : System.Math.Max(1, Summary.HotCount + Summary.WarmCount + Summary.ColdCount);
    public double HotFraction => Summary is null ? 0 : Summary.HotCount / (double)ClassTotal;
    public double WarmFraction => Summary is null ? 0 : Summary.WarmCount / (double)ClassTotal;
    public double ColdFraction => Summary is null ? 0 : Summary.ColdCount / (double)ClassTotal;

    /// <summary>Navigate callback set by the shell so attention rows / quick actions can jump screens. (GD-1)</summary>
    public System.Action<string>? NavigateTo { get; set; }

    /// <summary>Screen-head "Setup wizard" → onboarding dialog. (GD-1)</summary>
    [RelayCommand] private void SetupWizard() => launcher?.OpenOnboarding();

    /// <summary>Screen-head / attention "Rescue" → rescue dialog. (GD-1)</summary>
    [RelayCommand] private void Rescue() => launcher?.OpenRescue();

    /// <summary>Quick-action / attention-row navigation to another screen. (GD-1)</summary>
    [RelayCommand] private void Go(string screenId) => NavigateTo?.Invoke(screenId);

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Summary = await dashboard.GetSummaryAsync(cancellationToken).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(HotFraction));
        OnPropertyChanged(nameof(WarmFraction));
        OnPropertyChanged(nameof(ColdFraction));

        if (reclaim is not null)
        {
            long bytes = 0;
            foreach (var g in await reclaim.ExactGroupsAsync(cancellationToken).ConfigureAwait(true))
                bytes += g.Copies.Skip(1).Sum(c => c.SizeBytes);
            ReclaimableBytes = bytes;
        }

        if (activity is not null)
        {
            RecentActivity.Clear();
            foreach (var r in await activity.GetRecentAsync(6, cancellationToken).ConfigureAwait(true))
                RecentActivity.Add(r);
            OnPropertyChanged(nameof(HasRecentActivity));
        }
    }
}
