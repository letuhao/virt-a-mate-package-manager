using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.App.ViewModels;

/// <summary>SCR-4 · Loading presets: list, activation preview, switch. (16-checklist SCR-4.)</summary>
public sealed partial class PresetsViewModel(
    IPresetService presets, IProfileService? profiles = null, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    public ObservableCollection<PresetInfo> Presets { get; } = [];

    /// <summary>Member refs of the selected preset (member table). (GD-8)</summary>
    public ObservableCollection<string> Members { get; } = [];

    [ObservableProperty] private PresetInfo? _selected;
    [ObservableProperty] private ActivationPreview? _preview;
    [ObservableProperty] private string? _statusMessage;

    public bool IsEmpty => Presets.Count == 0;

    /// <summary>Screen-head "+ New preset" → preset-edit dialog on a fresh preset. (GD-8)</summary>
    [RelayCommand] private void NewPreset() => launcher?.OpenPresetEdit(0, "New preset");

    /// <summary>"Edit" → preset-edit dialog for the selected preset. (GD-8)</summary>
    [RelayCommand]
    private void Edit()
    {
        if (Selected is not null)
            launcher?.OpenPresetEdit(Selected.Id, Selected.Name);
    }

    /// <summary>"Deactivate all" → rescue baseline (drop all active links). (GD-8)</summary>
    [RelayCommand] private void DeactivateAll() => launcher?.OpenRescue();

    /// <summary>Screen-head "Import from txt…" → preset-edit dialog (import tab). (AC-20)</summary>
    [RelayCommand] private void ImportTxt() => launcher?.OpenPresetEdit(0, "Import from txt");

    /// <summary>The most recent export text (member refs, one per line). (AC-20)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Detail "Export" → member refs as a txt list. (AC-20)</summary>
    [RelayCommand]
    private void Export()
    {
        LastExportText = string.Join(System.Environment.NewLine, Members);
        StatusMessage = $"Exported {Members.Count} members";
    }

    /// <summary>Detail "Diff" → summarize this preset vs its resolved closure. (AC-20)</summary>
    [RelayCommand]
    private void Diff() =>
        StatusMessage = Preview is null
            ? "No preview loaded"
            : $"{Members.Count} direct → {Preview.TotalWithClosure} with closure, {Preview.MissingRefs.Count} missing";

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
        Members.Clear();
        if (preset is not null)
            foreach (var m in await presets.MembersAsync(preset.Id).ConfigureAwait(true))
                Members.Add(m);
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
