using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;

namespace VarVault.App.ViewModels;

/// <summary>DLG-9 · Rescue: deactivate all → clean minimal active set so VaM launches. (16-checklist DLG-9.)</summary>
public sealed partial class RescueViewModel(IActivationService activation) : ObservableObject
{
    [ObservableProperty] private long _profileId;
    [ObservableProperty] private string? _resultMessage;

    [RelayCommand]
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var deactivated = await activation.RescueAsync(ProfileId, cancellationToken).ConfigureAwait(true);
        ResultMessage = $"Deactivated {deactivated} packages — clean baseline applied";
    }
}
