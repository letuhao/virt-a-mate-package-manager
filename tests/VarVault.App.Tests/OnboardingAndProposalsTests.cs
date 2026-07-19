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
    private static Proposal P(long id, ProposalKind kind) => new(id, kind, $"proposal {id}", 1000);

    [Fact]
    public void Approve_and_reject_move_out_of_pending()
    {
        var vm = new ProposalsViewModel();
        vm.Load([P(1, ProposalKind.Migration), P(2, ProposalKind.Dedup), P(3, ProposalKind.EncodingFix)]);
        Assert.Equal(3, vm.PendingCount);

        vm.Approve(vm.Pending[0]);
        vm.Reject(vm.Pending[0]);

        Assert.Equal(1, vm.PendingCount);
        Assert.Single(vm.Approved);
        Assert.Single(vm.Rejected);
    }

    [Fact]
    public void Approve_all_clears_pending()
    {
        var vm = new ProposalsViewModel();
        vm.Load([P(1, ProposalKind.Migration), P(2, ProposalKind.Stale)]);
        vm.ApproveAll();
        Assert.Equal(0, vm.PendingCount);
        Assert.Equal(2, vm.Approved.Count);
    }
}
