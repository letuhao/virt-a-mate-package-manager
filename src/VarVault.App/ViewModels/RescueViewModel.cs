using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-9 · Rescue: deactivate all on the active profile → optional baseline activate &amp; switch. (16-checklist DLG-9.)</summary>
public sealed partial class RescueViewModel(
    IActivationService activation,
    Sdk.Presets.IPresetService? presets = null,
    IProfileService? profiles = null) : ObservableObject
{
    [ObservableProperty] private string? _resultMessage;

    /// <summary>Presets offered as the post-rescue baseline. (AC-27)</summary>
    public System.Collections.ObjectModel.ObservableCollection<Sdk.Presets.PresetInfo> BaselineOptions { get; } = [];
    [ObservableProperty] private Sdk.Presets.PresetInfo? _selectedBaseline;

    /// <summary>Load the baseline-preset choices from the preset service. (AC-27)</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (presets is null)
            return;
        BaselineOptions.Clear();
        foreach (var p in await presets.ListAsync(cancellationToken).ConfigureAwait(true))
            BaselineOptions.Add(p);
    }

    [RelayCommand]
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var rescued = await activation.RescueActiveAsync(cancellationToken).ConfigureAwait(true);
        if (rescued.IsFailure)
        {
            ResultMessage = rescued.Error.Message;
            return;
        }

        var deactivated = rescued.Value;
        if (SelectedBaseline is null)
        {
            ResultMessage = $"Deactivated {deactivated} packages — no baseline selected";
            return;
        }

        var build = await activation.BuildProfileLinksAsync(SelectedBaseline.Id, cancellationToken).ConfigureAwait(true);
        if (build.PathUnavailable > 0)
        {
            ResultMessage = $"Deactivated {deactivated} packages — baseline skipped (VaM path unset)";
            return;
        }
        if (build.PrivilegeFailures > 0)
        {
            ResultMessage = $"Deactivated {deactivated} packages — baseline activate needs Developer Mode / admin";
            return;
        }

        if (profiles is null)
        {
            ResultMessage =
                $"Deactivated {deactivated} packages · linked baseline '{SelectedBaseline.Name}' ({build.LinksCreated} links) — profile switch unavailable";
            return;
        }

        var switched = await profiles.SwitchToAsync(SelectedBaseline.Name, cancellationToken).ConfigureAwait(true);
        ResultMessage = switched.IsSuccess
            ? $"Deactivated {deactivated} packages · activated & switched to '{SelectedBaseline.Name}': {build.LinksCreated} linked"
            : $"Deactivated {deactivated} packages · baseline linked but switch failed: {switched.Error.Message}";
    }
}
