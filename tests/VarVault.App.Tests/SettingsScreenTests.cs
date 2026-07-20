using VarVault.Sdk.Settings;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-13 · Settings loads + saves through ISettingsService. (16-checklist SCR-13.)</summary>
[Trait("Category", TestCategories.Unit)]
public class SettingsScreenTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly Dictionary<string, string> _v = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string k, CancellationToken ct = default) => Task.FromResult(_v.GetValueOrDefault(k));
        public Task SetAsync(string k, string v, CancellationToken ct = default) { _v[k] = v; return Task.CompletedTask; }
        public Task<bool> GetBoolAsync(string k, bool f = false, CancellationToken ct = default) => Task.FromResult(f);
        public Task SetBoolAsync(string k, bool v, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<string, string>>(_v);
    }

    [Fact]
    public async Task Loads_and_saves_vam_path()
    {
        var settings = new FakeSettings();
        await settings.SetAsync(SettingKeys.VamPath, @"D:\VaM");
        var vm = new SettingsViewModel(settings);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(@"D:\VaM", vm.VamPath);

        vm.VamPath = @"E:\VaM";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(@"E:\VaM", await settings.GetAsync(SettingKeys.VamPath));
        Assert.Equal("Saved", vm.StatusMessage);
    }

    [Fact]
    public async Task Import_temp_dir_and_history_keep_round_trip()
    {
        var settings = new FakeSettings();
        await settings.SetAsync("import.temp_dir", @"E:\scratch\import");
        await settings.SetAsync("import.history_keep", "50");

        var vm = new SettingsViewModel(settings);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(@"E:\scratch\import", vm.ImportTempDir);
        Assert.Equal("50", vm.ImportHistoryKeep);

        vm.ImportTempDir = @"F:\tmp";
        vm.ImportHistoryKeep = "25";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(@"F:\tmp", await settings.GetAsync("import.temp_dir"));
        Assert.Equal("25", await settings.GetAsync("import.history_keep"));

        // A blank/invalid keep count clears the key so the engine default (200) applies.
        vm.ImportHistoryKeep = "not-a-number";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, await settings.GetAsync("import.history_keep"));
    }

    [Fact]
    public void Import_tab_is_selectable()
    {
        var vm = new SettingsViewModel(new FakeSettings()) { SelectedTabIndex = 3 };
        Assert.True(vm.IsImportTab);
    }
}
