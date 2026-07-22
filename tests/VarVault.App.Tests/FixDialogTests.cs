using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-4 · Fix calls IHealthService.FixAsync. (16-checklist DLG-4.)</summary>
[Trait("Category", TestCategories.Unit)]
public class FixDialogTests
{
    private class StubHealth : IHealthService
    {
        public virtual Task<Result<long>> FixAsync(long id, CancellationToken ct = default) => Task.FromResult(Result.Success(99L));
        public virtual Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(3, 0));
        public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, IProgressSink progress, CancellationToken ct = default) =>
            FixGroupAsync(codepageFilter, ct);
        public Task<BulkActionResult> FixManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(varFileIds.Count, 0));
        public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EncodingGroup>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
    }

    [Fact]
    public async Task Fix_reports_success()
    {
        var vm = new FixEncodingViewModel(new StubHealth()) { VarFileId = 5 };
        await vm.FixCommand.ExecuteAsync(null);
        Assert.Contains("UTF-8", vm.ResultMessage);
    }

    [Fact]
    public async Task Fix_with_codepage_applies_to_group()
    {
        var vm = new FixEncodingViewModel(new StubHealth()) { VarFileId = 0, Codepage = "GBK" };
        await vm.FixCommand.ExecuteAsync(null);
        Assert.Contains("Fixed 3", vm.ResultMessage);
    }

    [Fact]
    public async Task Fix_with_var_id_ignores_codepage_for_single_var()
    {
        var health = new CountingHealth();
        var vm = new FixEncodingViewModel(health) { VarFileId = 5, Codepage = "GBK" };
        await vm.FixCommand.ExecuteAsync(null);
        Assert.Equal(1, health.FixCalls);
        Assert.Equal(0, health.GroupCalls);
        Assert.Contains("UTF-8", vm.ResultMessage);
    }

    private sealed class CountingHealth : StubHealth
    {
        public int FixCalls;
        public int GroupCalls;
        public override Task<Result<long>> FixAsync(long id, CancellationToken ct = default)
        {
            FixCalls++;
            return base.FixAsync(id, ct);
        }
        public override Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken ct = default)
        {
            GroupCalls++;
            return base.FixGroupAsync(codepageFilter, ct);
        }
    }
}
