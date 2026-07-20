using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Domain.Activation;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>SCR-13 · Settings: VaM path (browse + validate), fix-on-import policy, etc. (16-checklist SCR-13; checklist 22 · T6.3a/b.)</summary>
public sealed partial class SettingsViewModel(ISettingsService settings) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("General"), new("Tiers & policy"), new("Automation"), new("Import"), new("Advanced")];
    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private string? _vamPath;
    [ObservableProperty] private string? _catalogDbPath;
    [ObservableProperty] private string? _fixOnImport;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Validation message for the VaM path (null when valid/blank). (T6.3a)</summary>
    [ObservableProperty] private string? _vamPathValidationMessage;

    /// <summary>Optional folder-picker hook the view sets to the real StorageProvider. (T6.3a)</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>True when the configured VaM path points at a real VaM install. (T6.3a)</summary>
    public bool IsVamPathValid => !string.IsNullOrWhiteSpace(VamPath) && ValidateVamPath(VamPath) is null;

    /// <summary>"Browse…" → pick the VaM install folder via the host picker. (T6.3a)</summary>
    [RelayCommand]
    public async Task BrowseVamPathAsync()
    {
        if (FolderPicker is null)
            return;
        var picked = await FolderPicker().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
            VamPath = picked;
    }

    partial void OnVamPathChanged(string? value)
    {
        VamPathValidationMessage = ValidateVamPath(value);
        OnPropertyChanged(nameof(IsVamPathValid));
    }

    /// <summary>Null = valid (or blank); otherwise a human-readable reason. Accepts VaM.exe OR AddonPackages OR
    /// ___AddonPacksSwitch ___ so both vanilla and already-managed installs pass. (T6.3a)</summary>
    private static string? ValidateVamPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null; // blank is allowed; Activate simply stays disabled until it's set
        if (!Directory.Exists(path))
            return "Folder not found.";
        var looksLikeVam = File.Exists(Path.Combine(path, "VaM.exe"))
            || Directory.Exists(Path.Combine(path, "AddonPackages"))
            || File.Exists(Path.Combine(path, "AddonPackages"))
            || Directory.Exists(Path.Combine(path, ActivationPaths.SwitchDirName));
        return looksLikeVam ? null : "Not a VaM install (VaM.exe / AddonPackages not found).";
    }

    // Additional per-tab settings (AC-21).
    [ObservableProperty] private string? _hotThresholdDays;       // Tiers & policy
    [ObservableProperty] private bool _autoRebalance;            // Automation
    [ObservableProperty] private string? _presetExtractionDir;    // Import

    // Per-tab visibility so each tab shows its own content (AC-21).
    public bool IsGeneralTab => SelectedTabIndex == 0;
    public bool IsTiersTab => SelectedTabIndex == 1;
    public bool IsAutomationTab => SelectedTabIndex == 2;
    public bool IsImportTab => SelectedTabIndex == 3;
    public bool IsAdvancedTab => SelectedTabIndex == 4;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsTiersTab));
        OnPropertyChanged(nameof(IsAutomationTab));
        OnPropertyChanged(nameof(IsImportTab));
        OnPropertyChanged(nameof(IsAdvancedTab));
    }

    /// <summary>Fix-on-import policy options (prototype dropdown). (GD-17)</summary>
    public IReadOnlyList<string> FixOnImportOptions { get; } = ["Flag only", "Prompt", "Auto (high-confidence)"];

    // NOTE: the former "Symlink strategy" dropdown was removed (T6.3b) — it was written to `symlink.type`
    // but never read. Activation always uses both mechanisms: profile-directory switch + per-var links.

    private const string CatalogDbKey = "catalog.db_path";
    private const string HotDaysKey = "tiers.hot_threshold_days";
    private const string AutoRebalanceKey = "automation.auto_rebalance";
    private const string PresetExtractKey = "import.preset_extraction_dir";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        VamPath = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(true);
        FixOnImport = await settings.GetAsync(SettingKeys.FixOnImport, cancellationToken).ConfigureAwait(true) ?? "Flag only";
        CatalogDbPath = await settings.GetAsync(CatalogDbKey, cancellationToken).ConfigureAwait(true);
        HotThresholdDays = await settings.GetAsync(HotDaysKey, cancellationToken).ConfigureAwait(true) ?? "30";
        AutoRebalance = await settings.GetBoolAsync(AutoRebalanceKey, false, cancellationToken).ConfigureAwait(true);
        PresetExtractionDir = await settings.GetAsync(PresetExtractKey, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        // Persist the path regardless (never lose user input). Invalidity is surfaced by the inline
        // VamPathValidationMessage (red hint); activation is guarded defensively in EfActivationService. (T6.3a)
        await settings.SetAsync(SettingKeys.VamPath, VamPath ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(SettingKeys.FixOnImport, FixOnImport ?? "Flag only", cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(CatalogDbKey, CatalogDbPath ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(HotDaysKey, HotThresholdDays ?? "30", cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(AutoRebalanceKey, AutoRebalance, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PresetExtractKey, PresetExtractionDir ?? string.Empty, cancellationToken).ConfigureAwait(true);
        StatusMessage = "Saved";
    }
}
