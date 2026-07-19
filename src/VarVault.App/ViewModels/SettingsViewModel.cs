using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>SCR-13 · Settings: VaM path, fix-on-import policy, etc. (16-checklist SCR-13.)</summary>
public sealed partial class SettingsViewModel(ISettingsService settings) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("General"), new("Tiers & policy"), new("Automation"), new("Import"), new("Advanced")];
    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private string? _vamPath;
    [ObservableProperty] private string? _catalogDbPath;
    [ObservableProperty] private string? _fixOnImport;
    [ObservableProperty] private string? _symlinkType;
    [ObservableProperty] private string? _statusMessage;

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

    /// <summary>Symlink strategy options (prototype dropdown). (GD-17)</summary>
    public IReadOnlyList<string> SymlinkOptions { get; } = ["Directory-swap profiles (fast)", "Per-var symlinks"];

    private const string CatalogDbKey = "catalog.db_path";
    private const string SymlinkKey = "symlink.type";
    private const string HotDaysKey = "tiers.hot_threshold_days";
    private const string AutoRebalanceKey = "automation.auto_rebalance";
    private const string PresetExtractKey = "import.preset_extraction_dir";

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        VamPath = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(true);
        FixOnImport = await settings.GetAsync(SettingKeys.FixOnImport, cancellationToken).ConfigureAwait(true) ?? "Flag only";
        CatalogDbPath = await settings.GetAsync(CatalogDbKey, cancellationToken).ConfigureAwait(true);
        SymlinkType = await settings.GetAsync(SymlinkKey, cancellationToken).ConfigureAwait(true) ?? "Directory-swap profiles (fast)";
        HotThresholdDays = await settings.GetAsync(HotDaysKey, cancellationToken).ConfigureAwait(true) ?? "30";
        AutoRebalance = await settings.GetBoolAsync(AutoRebalanceKey, false, cancellationToken).ConfigureAwait(true);
        PresetExtractionDir = await settings.GetAsync(PresetExtractKey, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await settings.SetAsync(SettingKeys.VamPath, VamPath ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(SettingKeys.FixOnImport, FixOnImport ?? "Flag only", cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(CatalogDbKey, CatalogDbPath ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(SymlinkKey, SymlinkType ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(HotDaysKey, HotThresholdDays ?? "30", cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(AutoRebalanceKey, AutoRebalance, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PresetExtractKey, PresetExtractionDir ?? string.Empty, cancellationToken).ConfigureAwait(true);
        StatusMessage = "Saved";
    }
}
