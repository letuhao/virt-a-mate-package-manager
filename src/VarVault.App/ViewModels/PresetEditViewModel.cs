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

    /// <summary>The preset's current members, shown as a table with remove buttons. (AC-25)</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Members { get; } = [];

    /// <summary>Most recent export text (member refs, one per line). (AC-25)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Load the member table from the preset service. (AC-25)</summary>
    public async Task LoadMembersAsync(CancellationToken cancellationToken = default)
    {
        Members.Clear();
        foreach (var m in await presets.MembersAsync(PresetId, cancellationToken).ConfigureAwait(true))
            Members.Add(m);
    }

    [RelayCommand]
    public async Task AddMemberAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(NewMemberRef))
            return;
        var result = await presets.AddMemberAsync(PresetId, NewMemberRef!, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Member added" : result.Error.Message;
        NewMemberRef = null;
        await LoadMembersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Member table "×" → remove a member. (AC-25)</summary>
    [RelayCommand]
    public async Task RemoveMemberAsync(string memberRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(memberRef))
            return;
        await presets.RemoveMemberAsync(PresetId, memberRef, cancellationToken).ConfigureAwait(true);
        await LoadMembersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>"Export" → member refs as a txt list. (AC-25)</summary>
    [RelayCommand]
    private void Export() => LastExportText = string.Join(System.Environment.NewLine, Members);

    [RelayCommand]
    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        Preview = await presets.PreviewActivationAsync(PresetId, cancellationToken).ConfigureAwait(true);
    }
}
