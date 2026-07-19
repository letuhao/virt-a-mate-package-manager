using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.App.ViewModels;

/// <summary>SCR-4 · Loading presets: list, activation preview, switch. (16-checklist SCR-4.)</summary>
public sealed partial class PresetsViewModel(IPresetService presets, IProfileService? profiles = null) : ObservableObject
{
    public ObservableCollection<PresetInfo> Presets { get; } = [];

    [ObservableProperty] private PresetInfo? _selected;
    [ObservableProperty] private ActivationPreview? _preview;
    [ObservableProperty] private string? _statusMessage;

    public bool IsEmpty => Presets.Count == 0;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Presets.Clear();
        foreach (var p in await presets.ListAsync(cancellationToken).ConfigureAwait(true))
            Presets.Add(p);
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedChanged(PresetInfo? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(PresetInfo? preset)
    {
        Preview = preset is null ? null : await presets.PreviewActivationAsync(preset.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SwitchAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || profiles is null)
            return;
        var result = await profiles.SwitchToAsync(Selected.Name, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? $"Switched to {Selected.Name}" : result.Error.Message;
    }
}
