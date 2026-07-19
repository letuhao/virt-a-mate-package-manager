using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-4 · Fix calls IHealthService.FixAsync. (16-checklist DLG-4.)</summary>
[Trait("Category", TestCategories.Unit)]
public class FixDialogTests
{
    private sealed class StubHealth : IHealthService
    {
        public Task<Result<long>> FixAsync(long id, CancellationToken ct = default) => Task.FromResult(Result.Success(99L));
        public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EncodingGroup>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
    }

    [Fact]
    public async Task Fix_reports_success()
    {
        var vm = new FixEncodingViewModel(new StubHealth()) { VarFileId = 5 };
        await vm.FixCommand.ExecuteAsync(null);
        Assert.Contains("UTF-8", vm.ResultMessage);
    }
}
