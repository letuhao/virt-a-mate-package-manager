using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class OnboardingViewModelTests
{
    [Fact]
    public void Advances_through_the_steps_to_done()
    {
        var vm = new OnboardingViewModel();
        Assert.Equal(OnboardingStep.AddDrives, vm.Step);
        Assert.False(vm.CanGoBack);

        vm.Next(); Assert.Equal(OnboardingStep.Benchmark, vm.Step);
        vm.Next(); vm.Next(); // Index, Rescue
        Assert.True(vm.CanGoBack);
        vm.Next(); // Done
        Assert.True(vm.IsComplete);
        Assert.False(vm.CanAdvance);
        Assert.False(vm.NextCommand.CanExecute(null));
    }

    [Fact]
    public void Back_returns_to_the_previous_step()
    {
        var vm = new OnboardingViewModel();
        vm.Next(); vm.Next(); // Index
        vm.Back();
        Assert.Equal(OnboardingStep.Benchmark, vm.Step);
    }
}

[Trait("Category", TestCategories.Unit)]
public class ProposalsViewModelTests
{
    private sealed class StubProposals : VarVault.Sdk.Library.IProposalService
    {
        public Task<IReadOnlyList<VarVault.Sdk.Library.Proposal>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VarVault.Sdk.Library.Proposal>>(
            [
                new("dedup:X", VarVault.Sdk.Library.ProposalKind.Dedup, "Remove 3 dups", "verified", 312, [1, 2, 3]),
                new("stale", VarVault.Sdk.Library.ProposalKind.RetireStale, "Retire 2", "→ trash", 88, [4]),
            ]);
        public Task<VarVault.Sdk.Library.ProposalActionResult> ApproveAsync(VarVault.Sdk.Library.Proposal p, CancellationToken ct = default) =>
            Task.FromResult(new VarVault.Sdk.Library.ProposalActionResult(true, "done"));
        public Task<VarVault.Sdk.Library.ProposalActionResult> RejectAsync(VarVault.Sdk.Library.Proposal p, CancellationToken ct = default) =>
            Task.FromResult(new VarVault.Sdk.Library.ProposalActionResult(true, "rejected"));
    }

    [Fact]
    public async Task Loads_then_approve_and_reject_remove_from_pending()
    {
        var vm = new ProposalsViewModel(new StubProposals());
        await vm.LoadAsync();
        Assert.Equal(2, vm.PendingCount);

        await vm.ApproveCommand.ExecuteAsync(vm.Pending[0]);
        Assert.Equal(1, vm.PendingCount);
        Assert.Equal("done", vm.StatusMessage);

        await vm.RejectCommand.ExecuteAsync(vm.Pending[0]);
        Assert.Equal(0, vm.PendingCount);
    }
}
