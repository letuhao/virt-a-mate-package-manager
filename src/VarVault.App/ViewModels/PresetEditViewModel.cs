using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Presets;

namespace VarVault.App.ViewModels;

/// <summary>DLG-7 · Edit a preset: add members, preview activation closure. (16-checklist DLG-7.)</summary>
public sealed partial class PresetEditViewModel(IPresetService presets) : ObservableObject
{
    [ObservableProperty] private long _presetId;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _newMemberRef;
    [ObservableProperty] private ActivationPreview? _preview;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public async Task AddMemberAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(NewMemberRef))
            return;
        var result = await presets.AddMemberAsync(PresetId, NewMemberRef!, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Member added" : result.Error.Message;
        NewMemberRef = null;
        await RefreshPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        Preview = await presets.PreviewActivationAsync(PresetId, cancellationToken).ConfigureAwait(true);
    }
}
