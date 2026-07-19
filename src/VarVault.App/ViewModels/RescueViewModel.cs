using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;

namespace VarVault.App.ViewModels;

/// <summary>DLG-9 · Rescue: deactivate all → clean minimal active set so VaM launches. (16-checklist DLG-9.)</summary>
public sealed partial class RescueViewModel(IActivationService activation, Sdk.Presets.IPresetService? presets = null)
    : ObservableObject
{
    [ObservableProperty] private long _profileId;
    [ObservableProperty] private string? _resultMessage;

    /// <summary>Presets offered as the post-rescue baseline (prototype dropdown). (AC-27)</summary>
    public System.Collections.ObjectModel.ObservableCollection<Sdk.Presets.PresetInfo> BaselineOptions { get; } = [];
    [ObservableProperty] private Sdk.Presets.PresetInfo? _selectedBaseline;

    /// <summary>Load the baseline-preset choices from the preset service. (AC-27)</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (presets is null)
            return;
        foreach (var p in await presets.ListAsync(cancellationToken).ConfigureAwait(true))
            BaselineOptions.Add(p);
    }

    [RelayCommand]
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var deactivated = await activation.RescueAsync(ProfileId, cancellationToken).ConfigureAwait(true);
        var baseline = SelectedBaseline is null ? "" : $" · baseline: {SelectedBaseline.Name}";
        ResultMessage = $"Deactivated {deactivated} packages — clean baseline applied{baseline}";
    }
}
