using VarVault.App.ViewModels;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class JobsViewModelTests
{
    [Fact]
    public void Refresh_lists_active_jobs_and_cancel_cancels()
    {
        var a = new JobHandle("index");
        var b = new JobHandle("migrate");
        var vm = new JobsViewModel(new FakeQueue(a, b));

        vm.Refresh();
        Assert.Equal(2, vm.Jobs.Count);
        Assert.True(vm.HasJobs);

        vm.Cancel(a);
        Assert.True(a.Cancellation.IsCancellationRequested);
    }

    private sealed class FakeQueue(params JobHandle[] active) : IJobQueue
    {
        public JobHandle Enqueue(string name, Func<JobContext, Task> work) => new(name);
        public IReadOnlyList<JobHandle> Active { get; } = active;
    }
}

[Trait("Category", TestCategories.Unit)]
public class ConfirmViewModelTests
{
    [Fact]
    public void Single_copy_cannot_be_confirmed()
    {
        var vm = new ConfirmViewModel { IsSingleCopy = true };
        Assert.False(vm.CanProceed);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.Contains("only copy", vm.ImpactSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reverse_dependent_impact_is_summarised()
    {
        var vm = new ConfirmViewModel { ReverseDependentCount = 5 };
        Assert.True(vm.CanProceed);
        Assert.Contains("5", vm.ImpactSummary, StringComparison.Ordinal);
        vm.Confirm();
        Assert.True(vm.Result);
    }

    [Fact]
    public void Foundational_impact_is_flagged()
    {
        var vm = new ConfirmViewModel { IsFoundational = true, ReverseDependentCount = 40 };
        Assert.Contains("Foundational", vm.ImpactSummary, StringComparison.Ordinal);
    }
}

[Trait("Category", TestCategories.Unit)]
public class UndoToastViewModelTests
{
    [Fact]
    public async Task Show_then_undo_invokes_and_hides()
    {
        var undone = false;
        var vm = new UndoToastViewModel();
        vm.Show("Trashed 3 files", _ => { undone = true; return Task.CompletedTask; });
        Assert.True(vm.IsVisible);

        await vm.UndoAsync();
        Assert.True(undone);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Dismiss_hides_without_undoing()
    {
        var vm = new UndoToastViewModel();
        vm.Show("x", _ => Task.CompletedTask);
        vm.Dismiss();
        Assert.False(vm.IsVisible);
    }
}

[Trait("Category", TestCategories.Unit)]
public class CommandPaletteViewModelTests
{
    [Fact]
    public void Query_filters_commands_and_invoke_runs()
    {
        var ran = false;
        var vm = new CommandPaletteViewModel(
        [
            new PaletteCommand("Index all repositories", "Index", () => { }),
            new PaletteCommand("Open settings", "App", () => ran = true),
        ]);

        Assert.Equal(2, vm.Results.Count);

        vm.Query = "settings";
        Assert.Single(vm.Results);
        Assert.Equal("Open settings", vm.Results[0].Name);

        vm.Invoke(vm.Results[0]);
        Assert.True(ran);
        Assert.False(vm.IsOpen);
    }
}
