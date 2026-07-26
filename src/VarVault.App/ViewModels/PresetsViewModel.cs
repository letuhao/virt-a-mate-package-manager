using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>A preset member ref + whether it currently resolves (for the member-table state pill). (doc 26 · F-9)</summary>
public sealed record PresetMemberRow(string Ref, bool IsMissing);

/// <summary>SCR-4 · Loading presets: list, activation preview, activate (materialize per-var symlinks), switch. (16-checklist SCR-4; checklist 22 · T6.1.)</summary>
public sealed partial class PresetsViewModel : ObservableObject, ILoadableScreen
{
    private readonly IPresetService _presets;
    private readonly IProfileService? _profiles;
    private readonly Services.IDialogLauncher? _launcher;
    private readonly IActivationService? _activation;
    private readonly ISettingsService? _settings;
    private CancellationTokenSource? _previewCts;

    public PresetsViewModel(
        IPresetService presets,
        IProfileService? profiles = null,
        Services.IDialogLauncher? launcher = null,
        IActivationService? activation = null,
        ISettingsService? settings = null)
    {
        _presets = presets;
        _profiles = profiles;
        _launcher = launcher;
        _activation = activation;
        _settings = settings;
        MembersPager = new PagedListState<PresetMemberRow>(LoadMembersPageAsync);
    }

    private const string DevModeHint =
        "Enable Windows Developer Mode (Settings → Privacy & security → For developers) or run elevated to create symlinks.";
    private const string PathHint =
        "VaM install path is not set or does not exist — set it in Settings before activating.";

    public PagedListState<PresetMemberRow> MembersPager { get; }

    public ObservableCollection<PresetInfo> Presets { get; } = [];

    /// <summary>Members of the selected preset with resolution state (member table). (GD-8 / doc 26 · F-9)</summary>
    public ObservableCollection<PresetMemberRow> Members => MembersPager.Items;

    [ObservableProperty] private PresetInfo? _selected;
    [ObservableProperty] private ActivationPreview? _preview;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>True once a VaM install path is configured — gates Activate (per-var links need a target). (T6.3)</summary>
    [ObservableProperty] private bool _vamPathConfigured;

    /// <summary>AddonPackages loading profiles (directories under ___AddonPacksSwitch ___) + the active one. (doc 26 · G-6)</summary>
    public ObservableCollection<string> Profiles { get; } = [];
    [ObservableProperty] private string? _activeProfile;
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string? _newProfileName;

    public bool IsEmpty => Presets.Count == 0;

    private bool CanActivate() => _activation is not null && Selected is not null && VamPathConfigured;

    /// <summary>"Activate" → materialize this preset's per-var symlinks under its profile's ___VarsLink___. (T6.1)</summary>
    [RelayCommand(CanExecute = nameof(CanActivate))]
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || _activation is null)
            return;
        var r = await _activation.BuildProfileLinksAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        StatusMessage = FormatActivationStatus(Selected.Name, r, switched: false);
    }

    /// <summary>"Activate &amp; switch" → materialize links, then repoint AddonPackages to this profile. (T6.1)</summary>
    [RelayCommand(CanExecute = nameof(CanActivate))]
    public async Task ActivateAndSwitchAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || _activation is null)
            return;
        var r = await _activation.BuildProfileLinksAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        if (r.PathUnavailable > 0)
        {
            StatusMessage = PathHint;
            return;
        }
        if (r.PrivilegeFailures > 0)
        {
            StatusMessage = DevModeHint;
            return;
        }
        if (_profiles is null)
        {
            StatusMessage = FormatActivationStatus(Selected.Name, r, switched: false);
            return;
        }
        var switched = await _profiles.SwitchToAsync(Selected.Name, cancellationToken).ConfigureAwait(true);
        StatusMessage = switched.IsSuccess
            ? FormatActivationStatus(Selected.Name, r, switched: true)
            : switched.Error.Message;
    }

    private static string FormatActivationStatus(string name, ActivationBuildResult r, bool switched)
    {
        if (r.PathUnavailable > 0)
            return PathHint;
        if (r.PrivilegeFailures > 0)
            return DevModeHint;
        var verb = switched ? $"Activated & switched to {name}" : $"Activated {name}";
        return switched
            ? $"{verb}: {r.LinksCreated} linked · {r.MissingPackages} offline · {r.UnresolvedDependencies} unresolved"
            : $"{verb}: {r.LinksCreated} linked · {r.MissingPackages} offline · {r.UnresolvedDependencies} unresolved · {r.LinksRemoved} removed";
    }

    /// <summary>Screen-head "+ New preset" → create then open edit on the real id. (GD-8)</summary>
    [RelayCommand]
    private async Task NewPresetAsync(CancellationToken cancellationToken = default)
    {
        var name = await UniquePresetNameAsync("New preset", cancellationToken).ConfigureAwait(true);
        var created = await _presets.CreateAsync(name, [], cancellationToken).ConfigureAwait(true);
        if (created.IsFailure)
        {
            StatusMessage = created.Error.Message;
            return;
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
        Selected = Presets.FirstOrDefault(p => p.Id == created.Value.Id) ?? created.Value;
        StatusMessage = $"Created '{created.Value.Name}'";
        _launcher?.OpenPresetEdit(created.Value.Id, created.Value.Name);
    }

    /// <summary>"Edit" → preset-edit dialog for the selected preset. (GD-8)</summary>
    [RelayCommand]
    private void Edit()
    {
        if (Selected is not null)
            _launcher?.OpenPresetEdit(Selected.Id, Selected.Name);
    }

    /// <summary>"Deactivate all" → rescue baseline (drop all active links). (GD-8)</summary>
    [RelayCommand] private void DeactivateAll() => _launcher?.OpenRescue();

    /// <summary>Open-file picker hook (set by the view) for "Import from txt…".</summary>
    public Func<Task<string?>>? OpenTxtPicker { get; set; }

    /// <summary>Save-file picker hook (set by the view) for Export.</summary>
    public Func<string, Task<string?>>? SaveTxtPicker { get; set; }

    /// <summary>Screen-head "Import from txt…" → create preset from member refs. (AC-20)</summary>
    [RelayCommand]
    private async Task ImportTxtAsync(CancellationToken cancellationToken = default)
    {
        if (OpenTxtPicker is null)
        {
            StatusMessage = "Import picker not ready — try again.";
            return;
        }
        var path = await OpenTxtPicker().ConfigureAwait(true);
        if (path is not null && path.Length == 0)
        {
            StatusMessage = "Import failed: window not ready for file picker.";
            return;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Import cancelled";
            return;
        }
        IReadOnlyList<string> refs;
        try
        {
            refs = await Services.TxtFileIo.ReadRefLinesAsync(path, cancellationToken).ConfigureAwait(true);
        }
        catch (System.IO.IOException ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            return;
        }
        var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Imported";
        var name = await UniquePresetNameAsync(baseName, cancellationToken).ConfigureAwait(true);
        var created = await _presets.CreateAsync(name, refs, cancellationToken).ConfigureAwait(true);
        if (created.IsFailure)
        {
            StatusMessage = created.Error.Message;
            return;
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
        Selected = Presets.FirstOrDefault(p => p.Id == created.Value.Id) ?? created.Value;
        StatusMessage = refs.Count == 0
            ? $"Created empty preset '{created.Value.Name}' (no refs in file)"
            : created.Value.MemberCount < refs.Count
                ? $"Imported {created.Value.MemberCount} of {refs.Count} refs → '{created.Value.Name}'"
                : $"Imported {created.Value.MemberCount} refs → '{created.Value.Name}'";
        _launcher?.OpenPresetEdit(created.Value.Id, created.Value.Name);
    }

    /// <summary>The most recent export text (member refs, one per line). (AC-20)</summary>
    [ObservableProperty] private string? _lastExportText;

    /// <summary>Detail "Export" → all member refs as a txt file. (AC-20)</summary>
    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null)
        {
            StatusMessage = "No preset selected";
            return;
        }
        try
        {
            var members = await _presets.MembersAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
            var text = string.Join(System.Environment.NewLine, members);
            var name = $"{SanitizeFileName(Selected.Name)}.txt";
            StatusMessage = await Services.TxtFileIo.ExportAsync(
                SaveTxtPicker, name, text, t => LastExportText = t, cancellationToken).ConfigureAwait(true);
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

    private async Task<string> UniquePresetNameAsync(string preferred, CancellationToken cancellationToken)
    {
        var existing = (await _presets.ListAsync(cancellationToken).ConfigureAwait(true))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(preferred))
            return preferred;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{preferred} {i}";
            if (!existing.Contains(candidate))
                return candidate;
        }
        return $"{preferred} {System.Guid.NewGuid():N}";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "preset-members" : name;
    }

    /// <summary>Detail "Diff" → summarize this preset vs its resolved closure. (AC-20)</summary>
    [RelayCommand]
    private void Diff() =>
        StatusMessage = Preview is null
            ? "No preview loaded"
            : $"{Preview.DirectResolved} direct → {Preview.TotalWithClosure} with closure, {Preview.MissingRefs.Count} missing";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Presets.Clear();
        foreach (var p in await _presets.ListAsync(cancellationToken).ConfigureAwait(true))
            Presets.Add(p);
        OnPropertyChanged(nameof(IsEmpty));

        if (_settings is not null)
        {
            var vamPath = await _settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(true);
            VamPathConfigured = !string.IsNullOrWhiteSpace(vamPath);
        }

        await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
    }

    // ── AddonPackages profile switcher (doc 26 · G-6) ────────────────────────────
    private async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (_profiles is null)
            return;
        Profiles.Clear();
        foreach (var p in await _profiles.ListAsync(cancellationToken).ConfigureAwait(true))
            Profiles.Add(p);
        ActiveProfile = await _profiles.ActiveAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Switch the active AddonPackages profile to the selected one (repoints one symlink).</summary>
    [RelayCommand]
    public async Task SwitchProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_profiles is null || string.IsNullOrWhiteSpace(SelectedProfile))
            return;
        var r = await _profiles.SwitchToAsync(SelectedProfile!, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? $"Switched profile → {SelectedProfile}" : r.Error.Message;
        if (r.IsSuccess)
            ActiveProfile = SelectedProfile;
    }

    /// <summary>Add a new empty AddonPackages profile named <see cref="NewProfileName"/>.</summary>
    [RelayCommand]
    public async Task AddProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_profiles is null || string.IsNullOrWhiteSpace(NewProfileName))
            return;
        var r = await _profiles.CreateAsync(NewProfileName!, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? $"Added profile '{NewProfileName}'" : r.Error.Message;
        if (r.IsSuccess)
        {
            NewProfileName = null;
            await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Rename the selected profile to <see cref="NewProfileName"/>.</summary>
    [RelayCommand]
    public async Task RenameProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_profiles is null || string.IsNullOrWhiteSpace(SelectedProfile) || string.IsNullOrWhiteSpace(NewProfileName))
            return;
        var r = await _profiles.RenameAsync(SelectedProfile!, NewProfileName!, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? $"Renamed → {NewProfileName}" : r.Error.Message;
        if (r.IsSuccess)
        {
            NewProfileName = null;
            await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Delete the selected profile (blocked for the active profile by the service).</summary>
    [RelayCommand]
    public async Task DeleteProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_profiles is null || string.IsNullOrWhiteSpace(SelectedProfile))
            return;
        var r = await _profiles.DeleteAsync(SelectedProfile!, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? $"Deleted profile '{SelectedProfile}'" : r.Error.Message;
        if (r.IsSuccess)
            await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedChanged(PresetInfo? value)
    {
        ActivateCommand.NotifyCanExecuteChanged();
        ActivateAndSwitchCommand.NotifyCanExecuteChanged();
        _ = LoadPreviewAsync(value);
    }

    partial void OnVamPathConfiguredChanged(bool value)
    {
        ActivateCommand.NotifyCanExecuteChanged();
        ActivateAndSwitchCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadPreviewAsync(PresetInfo? preset)
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var token = cts.Token;

        try
        {
            if (preset is null)
            {
                Preview = null;
                Members.Clear();
                return;
            }

            var preview = await _presets.PreviewActivationAsync(preset.Id, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || !ReferenceEquals(Selected, preset))
                return;
            Preview = preview;
            await MembersPager.ResetAndReloadAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded by a newer selection.
        }
    }

    [RelayCommand]
    public async Task SwitchAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || _profiles is null)
            return;
        var result = await _profiles.SwitchToAsync(Selected.Name, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? $"Switched to {Selected.Name}" : result.Error.Message;
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

    private async Task<PageResult<PresetMemberRow>> LoadMembersPageAsync(PageRequest request, CancellationToken ct)
    {
        if (Selected is null)
            return PageResult<PresetMemberRow>.Empty(request);
        var preview = Preview ?? await _presets.PreviewActivationAsync(Selected.Id, ct).ConfigureAwait(false);
        var missing = new HashSet<string>(preview?.MissingRefs ?? [], StringComparer.OrdinalIgnoreCase);
        var page = await _presets.MembersPageAsync(Selected.Id, request, ct).ConfigureAwait(false);
        return new PageResult<PresetMemberRow>(
            page.Items.Select(m => new PresetMemberRow(m, missing.Contains(m))).ToList(),
            page.TotalCount,
            page.PageNumber,
            page.PageSize);
    }
}
