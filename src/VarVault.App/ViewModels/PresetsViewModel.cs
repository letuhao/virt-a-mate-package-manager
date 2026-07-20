using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>A preset member ref + whether it currently resolves (for the member-table state pill). (doc 26 · F-9)</summary>
public sealed record PresetMemberRow(string Ref, bool IsMissing);

/// <summary>SCR-4 · Loading presets: list, activation preview, activate (materialize per-var symlinks), switch. (16-checklist SCR-4; checklist 22 · T6.1.)</summary>
public sealed partial class PresetsViewModel(
    IPresetService presets,
    IProfileService? profiles = null,
    Services.IDialogLauncher? launcher = null,
    IActivationService? activation = null,
    ISettingsService? settings = null) : ObservableObject, ILoadableScreen
{
    private const string DevModeHint =
        "Enable Windows Developer Mode (Settings → Privacy & security → For developers) or run elevated to create symlinks.";

    public ObservableCollection<PresetInfo> Presets { get; } = [];

    /// <summary>Members of the selected preset with resolution state (member table). (GD-8 / doc 26 · F-9)</summary>
    public ObservableCollection<PresetMemberRow> Members { get; } = [];

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

    private bool CanActivate() => activation is not null && Selected is not null && VamPathConfigured;

    /// <summary>"Activate" → materialize this preset's per-var symlinks under its profile's ___VarsLink___. (T6.1)</summary>
    [RelayCommand(CanExecute = nameof(CanActivate))]
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || activation is null)
            return;
        var r = await activation.BuildProfileLinksAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.PrivilegeFailures > 0
            ? DevModeHint
            : $"Activated {Selected.Name}: {r.LinksCreated} linked · {r.MissingPackages} missing · {r.LinksRemoved} removed";
    }

    /// <summary>"Activate &amp; switch" → materialize links, then repoint AddonPackages to this profile. (T6.1)</summary>
    [RelayCommand(CanExecute = nameof(CanActivate))]
    public async Task ActivateAndSwitchAsync(CancellationToken cancellationToken = default)
    {
        if (Selected is null || activation is null)
            return;
        var r = await activation.BuildProfileLinksAsync(Selected.Id, cancellationToken).ConfigureAwait(true);
        if (r.PrivilegeFailures > 0)
        {
            StatusMessage = DevModeHint;
            return;
        }
        if (profiles is null)
        {
            StatusMessage = $"Activated {Selected.Name}: {r.LinksCreated} linked";
            return;
        }
        var switched = await profiles.SwitchToAsync(Selected.Name, cancellationToken).ConfigureAwait(true);
        StatusMessage = switched.IsSuccess
            ? $"Activated & switched to {Selected.Name}: {r.LinksCreated} linked · {r.MissingPackages} missing"
            : switched.Error.Message;
    }

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
        LastExportText = string.Join(System.Environment.NewLine, Members.Select(m => m.Ref));
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

        if (settings is not null)
        {
            var vamPath = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(true);
            VamPathConfigured = !string.IsNullOrWhiteSpace(vamPath);
        }

        await ReloadProfilesAsync(cancellationToken).ConfigureAwait(true);
    }

    // ── AddonPackages profile switcher (doc 26 · G-6) ────────────────────────────
    private async Task ReloadProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (profiles is null)
            return;
        Profiles.Clear();
        foreach (var p in await profiles.ListAsync(cancellationToken).ConfigureAwait(true))
            Profiles.Add(p);
        ActiveProfile = await profiles.ActiveAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Switch the active AddonPackages profile to the selected one (repoints one symlink).</summary>
    [RelayCommand]
    public async Task SwitchProfileAsync(CancellationToken cancellationToken = default)
    {
        if (profiles is null || string.IsNullOrWhiteSpace(SelectedProfile))
            return;
        var r = await profiles.SwitchToAsync(SelectedProfile!, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? $"Switched profile → {SelectedProfile}" : r.Error.Message;
        if (r.IsSuccess)
            ActiveProfile = SelectedProfile;
    }

    /// <summary>Add a new empty AddonPackages profile named <see cref="NewProfileName"/>.</summary>
    [RelayCommand]
    public async Task AddProfileAsync(CancellationToken cancellationToken = default)
    {
        if (profiles is null || string.IsNullOrWhiteSpace(NewProfileName))
            return;
        var r = await profiles.CreateAsync(NewProfileName!, cancellationToken).ConfigureAwait(true);
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
        if (profiles is null || string.IsNullOrWhiteSpace(SelectedProfile) || string.IsNullOrWhiteSpace(NewProfileName))
            return;
        var r = await profiles.RenameAsync(SelectedProfile!, NewProfileName!, cancellationToken).ConfigureAwait(true);
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
        if (profiles is null || string.IsNullOrWhiteSpace(SelectedProfile))
            return;
        var r = await profiles.DeleteAsync(SelectedProfile!, cancellationToken).ConfigureAwait(true);
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
        Preview = preset is null ? null : await presets.PreviewActivationAsync(preset.Id).ConfigureAwait(true);
        Members.Clear();
        if (preset is not null)
        {
            var missing = new HashSet<string>(Preview?.MissingRefs ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var m in await presets.MembersAsync(preset.Id).ConfigureAwait(true))
                Members.Add(new PresetMemberRow(m, missing.Contains(m)));
        }
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
