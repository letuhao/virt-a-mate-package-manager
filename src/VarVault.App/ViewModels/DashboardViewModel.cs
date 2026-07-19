using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-1 · Dashboard: summary tiles from <see cref="IDashboardService"/>. (16-checklist SCR-1.)</summary>
public sealed partial class DashboardViewModel(
    IDashboardService dashboard, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    [ObservableProperty] private DashboardSummary? _summary;

    public bool HasSummary => Summary is not null;

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
    }
}
