using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Import;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class ImportQuickImportGateTests
{
    private sealed class StubRepos : IRepositoryService
    {
        private readonly RepositoryInfo _repo = new(Guid.NewGuid(), "Hot", @"C:\repo", "SSD", 1, true, true, 0, 0, null);
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RepositoryInfo>>([_repo]);
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RenameAsync(Guid id, string name, CancellationToken ct = default) => Task.FromResult(true);
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => Task.FromResult<RepositoryInfo?>(null);
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => Task.FromResult<RepositoryInfo?>(null);
        public Task<RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => Task.FromResult<RepositoryInfo?>(null);
    }

    private sealed class StubLocator(LooseVarsLocateResult result) : IAddonPackagesLooseVarsLocator
    {
        public Task<LooseVarsLocateResult> LocateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubImport : IImportService
    {
        public Task<ImportSession> ScanAsync(ImportSpec spec, IProgressSink? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportSession(Guid.NewGuid(), Path.GetTempPath(), spec.TargetRepositoryId, ImportActivateMode.Off, [], []));
        public Task<ApplyResult> ApplyAsync(ImportSession session, IProgressSink? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplyResult(0, 0, 0, 0, 0, 0, Guid.NewGuid()));
        public Task<IReadOnlyList<ImportRun>> HistoryAsync(int take = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ImportRun>>([]);
        public Task<PageResult<ImportOutcome>> HistoryOutcomesPageAsync(
            Guid runId,
            PageRequest request,
            string filter = "all",
            string? searchText = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<ImportOutcome>([], 0, 1, request.SafePageSize));
        public Task SweepTempWorkspacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class InlineUi : IUiDispatcher
    {
        public bool IsOnUiThread => true;
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
        public Task<T> InvokeAsync<T>(Func<Task<T>> action) => action();
    }

    [Fact]
    public async Task RefreshQuickImportHint_surfaces_locator_gate()
    {
        var locator = new StubLocator(new LooseVarsLocateResult(
            LooseVarsLocateStatus.NoVamPath, null, null, 0, "Set VaM path in Settings"));
        // ImportJobRunner requires queue — pass a dummy via creating without calling Scan.
        var jobs = new ImportJobRunner(
            new NoopJobQueue(),
            new SimpleScopeFactory());
        var vm = new ImportViewModel(new StubImport(), new StubRepos(), jobs, new InlineUi(), locator);
        await vm.LoadAsync();
        Assert.True(vm.CanQuickImport);
        Assert.Equal("Set VaM path in Settings", vm.QuickImportHint);
    }

    private sealed class NoopJobQueue : IJobQueue
    {
        public JobHandle Enqueue(string title, Func<JobContext, Task> work) =>
            throw new NotSupportedException();
        public IReadOnlyList<JobHandle> Active => [];
    }

    private sealed class SimpleScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => throw new NotSupportedException();
    }
}
