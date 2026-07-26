using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Presets;

namespace VarVault.App.ViewModels;

/// <summary>DLG-7 · Edit a preset: add members, preview activation closure. (16-checklist DLG-7.)</summary>
public sealed partial class PresetEditViewModel : ObservableObject
{
    private readonly IPresetService _presets;

    public PresetEditViewModel(IPresetService presets)
    {
        _presets = presets;
        MembersPager = new PagedListState<string>((request, ct) => _presets.MembersPageAsync(PresetId, request, ct));
    }

    [ObservableProperty] private long _presetId;
    [ObservableProperty] private string? _name;
    [ObservableProperty] private string? _newMemberRef;
    [ObservableProperty] private ActivationPreview? _preview;
    [ObservableProperty] private string? _statusMessage;

    public PagedListState<string> MembersPager { get; }

    /// <summary>The preset's current members, shown as a table with remove buttons. (AC-25)</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Members => MembersPager.Items;

    /// <summary>Most recent export text (member refs, one per line). (AC-25)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Save-file picker hook (set by the view).</summary>
    public Func<string, Task<string?>>? SaveTxtPicker { get; set; }

    /// <summary>Load the member table from the preset service. (AC-25)</summary>
    public async Task LoadMembersAsync(CancellationToken cancellationToken = default)
    {
        if (PresetId <= 0)
            return;
        await MembersPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task AddMemberAsync(CancellationToken cancellationToken = default)
    {
        if (PresetId <= 0 || string.IsNullOrWhiteSpace(NewMemberRef))
            return;
        var result = await _presets.AddMemberAsync(PresetId, NewMemberRef!, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Member added" : result.Error.Message;
        NewMemberRef = null;
        await LoadMembersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Member table "×" → remove a member. (AC-25)</summary>
    [RelayCommand]
    public async Task RemoveMemberAsync(string memberRef, CancellationToken cancellationToken = default)
    {
        if (PresetId <= 0 || string.IsNullOrWhiteSpace(memberRef))
            return;
        await _presets.RemoveMemberAsync(PresetId, memberRef, cancellationToken).ConfigureAwait(true);
        await LoadMembersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshPreviewAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>"Export" → all member refs as a txt file. (AC-25)</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        if (PresetId <= 0)
        {
            StatusMessage = "Preset not loaded";
            return;
        }
        try
        {
            var members = await _presets.MembersAsync(PresetId, cancellationToken).ConfigureAwait(true);
            var text = string.Join(System.Environment.NewLine, members);
            var fileName = string.IsNullOrWhiteSpace(Name) ? "preset-members.txt" : $"{SanitizeFileName(Name)}.txt";
            StatusMessage = await TxtFileIo.ExportAsync(
                SaveTxtPicker, fileName, text, t => LastExportText = t, cancellationToken).ConfigureAwait(true);
        }
        catch (System.IO.IOException ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (PresetId <= 0)
        {
            Preview = null;
            return;
        }
        Preview = await _presets.PreviewActivationAsync(PresetId, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanMembersPreviousPage))]
    private async Task MembersPreviousPageAsync(CancellationToken cancellationToken = default) =>
        await MembersPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanMembersNextPage))]
    private async Task MembersNextPageAsync(CancellationToken cancellationToken = default) =>
        await MembersPager.NextPageAsync(cancellationToken).ConfigureAwait(true);

    [RelayCommand]
    private async Task MembersGoToPageAsync(int pageNumber) =>
        await MembersPager.LoadPageAsync(pageNumber, MembersPager.PageSize).ConfigureAwait(true);

    [RelayCommand]
    private async Task MembersChangePageSizeAsync(int pageSize) =>
        await MembersPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);

    private bool CanMembersPreviousPage() => MembersPager.HasPreviousPage && !MembersPager.IsLoading;
    private bool CanMembersNextPage() => MembersPager.HasNextPage && !MembersPager.IsLoading;

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "preset-members" : name;
    }
}
