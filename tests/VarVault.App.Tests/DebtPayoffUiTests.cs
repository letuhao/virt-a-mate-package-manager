using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class AddRepoReserveParseTests
{
    [Theory]
    [InlineData("200 GB", 200L * 1024 * 1024 * 1024)]
    [InlineData("1.5 TB", (long)(1.5 * 1024 * 1024 * 1024 * 1024))]
    [InlineData("512", 512L * 1024 * 1024 * 1024)] // bare number = GB
    [InlineData("", null)]
    [InlineData("200 G", null)]
    [InlineData("abc", null)]
    public void ParseReserveBytes_handles_units(string text, long? expected)
    {
        Assert.Equal(expected, AddRepoViewModel.ParseReserveBytes(text));
    }

    [Fact]
    public async Task Invalid_reserve_rejects_add_without_registering()
    {
        var stub = new CapturingRepos();
        var vm = new AddRepoViewModel(stub)
        {
            FolderPath = @"G:\vars",
            Reserve = "200 G",
        };
        await vm.AddCommand.ExecuteAsync(null);
        Assert.Null(stub.Last);
        Assert.Contains("Couldn't parse reserve", vm.Message);
    }

    [Fact]
    public async Task Add_passes_MinFreeBytes_and_PreferredTier()
    {
        var stub = new CapturingRepos();
        var vm = new AddRepoViewModel(stub)
        {
            FolderPath = @"G:\vars",
            Reserve = "100 GB",
            PreferredTier = "T1 (hot)",
        };
        await vm.AddCommand.ExecuteAsync(null);
        Assert.NotNull(stub.Last);
        Assert.Equal(100L * 1024 * 1024 * 1024, stub.Last!.MinFreeBytes);
        Assert.Equal(1, stub.Last.PreferredTier);
    }

    [Fact]
    public async Task Rebalance_checkbox_invokes_migration_service()
    {
        var mig = new CapturingMigration();
        var vm = new AddRepoViewModel(new CapturingRepos(), migration: mig)
        {
            FolderPath = @"G:\vars",
            RebalanceExisting = true,
            PreferredTier = "T2 (warm)",
        };
        await vm.AddCommand.ExecuteAsync(null);
        Assert.True(mig.Called);
    }

    private sealed class CapturingRepos : IRepositoryService
    {
        public RegisterRepositoryRequest? Last { get; private set; }
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default)
        {
            Last = r;
            return Task.FromResult(Result.Success(new RepositoryInfo(
                Guid.NewGuid(), r.Name, r.Path, "Nvme", r.PreferredTier ?? 1, true, true, 8000, 5000, null)));
        }
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RepositoryInfo>>([]);
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RenameAsync(Guid id, string name, CancellationToken ct = default) => Task.FromResult(true);
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class CapturingMigration : IMigrationService
    {
        public bool Called { get; private set; }
        public Task<MigrationRunResult> RunAsync(IReadOnlyList<MigrationRequest> moves, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MigrationRunResult(0, 0));
        public Task<MigrationRunResult> RebalanceOntoRepositoryAsync(Guid targetRepositoryId, CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(new MigrationRunResult(2, 0));
        }
    }
}

[Trait("Category", TestCategories.Unit)]
public class SettingsVamLogAndAutoRebalanceTests
{
    [Fact]
    public async Task Auto_rebalance_round_trips_through_settings()
    {
        var store = new MemSettings();
        var vm = new SettingsViewModel(store);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.AutoRebalance = true;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.True(await store.GetBoolAsync(SettingKeys.AutoRebalance));

        var reloaded = new SettingsViewModel(store);
        await reloaded.LoadCommand.ExecuteAsync(null);
        Assert.True(reloaded.AutoRebalance);
    }

    [Fact]
    public async Task Preview_and_import_call_vam_log_importer()
    {
        var importer = new CapturingImporter();
        var vm = new SettingsViewModel(new MemSettings(), importer) { VamLogText = "Creator.Look.1" };
        await vm.PreviewVamLogUsageCommand.ExecuteAsync(null);
        Assert.True(importer.Previewed);
        await vm.ImportVamLogUsageCommand.ExecuteAsync(null);
        Assert.True(importer.Imported);
        Assert.Contains("Imported", vm.VamLogStatus);
    }

    private sealed class MemSettings : ISettingsService
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_map.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _map[key] = value;
            return Task.CompletedTask;
        }
        public Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(_map.TryGetValue(key, out var v) ? v is "1" or "true" : fallback);
        public Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default)
        {
            _map[key] = value ? "true" : "false";
            return Task.CompletedTask;
        }
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(_map);
    }

    private sealed class CapturingImporter : IVamLogUsageImporter
    {
        public bool Previewed { get; private set; }
        public bool Imported { get; private set; }
        public Task<VamLogUsagePreview> PreviewAsync(string logText, CancellationToken cancellationToken = default)
        {
            Previewed = true;
            return Task.FromResult(new VamLogUsagePreview(1, 1, 0, ["Creator.Look.1"]));
        }
        public Task<VamLogUsageImportResult> ImportAsync(string logText, CancellationToken cancellationToken = default)
        {
            Imported = true;
            return Task.FromResult(new VamLogUsageImportResult(1, 1, 0));
        }
    }
}
