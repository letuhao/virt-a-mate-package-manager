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
    [ObservableProperty] private string? _fixOnImport;
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        VamPath = await settings.GetAsync(SettingKeys.VamPath, cancellationToken).ConfigureAwait(true);
        FixOnImport = await settings.GetAsync(SettingKeys.FixOnImport, cancellationToken).ConfigureAwait(true) ?? "Flag only";
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await settings.SetAsync(SettingKeys.VamPath, VamPath ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(SettingKeys.FixOnImport, FixOnImport ?? "Flag only", cancellationToken).ConfigureAwait(true);
        StatusMessage = "Saved";
    }
}
