using VarVault.Common;
using VarVault.Sdk.Repositories;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-2 · Add-repo registers + shows detected media/tier. (16-checklist DLG-2.)</summary>
[Trait("Category", TestCategories.Unit)]
public class AddRepoDialogTests
{
    private sealed class StubRepos : IRepositoryService
    {
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new RepositoryInfo(Guid.NewGuid(), r.Name, r.Path, "Hdd", 3, true, true, 8000, 5000, null)));
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RepositoryInfo>>([]);
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Add_registers_and_reports_detected_tier()
    {
        var vm = new AddRepoViewModel(new StubRepos()) { FolderPath = @"G:\vars" };
        await vm.AddCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Registered);
        Assert.Contains("Tier 3", vm.Message);
    }
}
