using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;
using Xunit;

namespace VarVault.App.Tests;

/// <summary>
/// 24-checklist A1/A2/A11 · The four screens whose tabs were decorative now swap content, Proposals filters by
/// category, and DupeReview lets the user choose which copy survives. Wired, not drawn (HR-G0): each test drives a
/// real ViewModel and asserts the wire executes.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class GapATabWiringTests
{
    [Fact]
    public void Dupes_tabs_swap_the_visible_panel()
    {
        var vm = new DupesViewModel(new StubReclaim());
        Assert.True(vm.IsReclaimTab);
        vm.SelectedTabIndex = 1;
        Assert.True(vm.IsExactTab);
        Assert.False(vm.IsReclaimTab);
        vm.SelectedTabIndex = 2;
        Assert.True(vm.IsNearTab);
        vm.SelectedTabIndex = 3;
        Assert.True(vm.IsIntakeTab);
    }

    [Fact]
    public void Health_tabs_swap_the_visible_panel()
    {
        var vm = new HealthViewModel(new StubHealth());
        Assert.True(vm.IsEncodingTab);
        vm.SelectedTabIndex = 1;
        Assert.True(vm.IsIntegrityTab);
        vm.SelectedTabIndex = 2;
        Assert.True(vm.IsMissingMetaTab);
        Assert.False(vm.IsEncodingTab);
    }

    [Fact]
    public void Tiering_tabs_swap_the_visible_panel()
    {
        var vm = new TieringViewModel(new StubTiering());
        Assert.True(vm.IsOverviewTab);
        vm.SelectedTabIndex = 1;
        Assert.True(vm.IsLifecycleTab);
        vm.SelectedTabIndex = 2;
        Assert.True(vm.IsPlacementTab);
        vm.SelectedTabIndex = 3;
        Assert.True(vm.IsStaleTab);
    }

    [Fact]
    public async Task Proposals_category_tab_filters_the_list()
    {
        var pending = new List<Proposal>
        {
            new("a", ProposalKind.Rebalance, "mig", "", 0, [1]),
            new("b", ProposalKind.Dedup, "dup", "", 0, [2]),
            new("c", ProposalKind.EncodingFix, "enc", "", 0, [3]),
            new("d", ProposalKind.Dedup, "dup2", "", 0, [4]),
        };
        var vm = new ProposalsViewModel(new StubProposals(pending));
        await vm.LoadAsync();

        Assert.Equal(4, vm.VisibleProposals.Count());   // "All" tab
        vm.SelectedTabIndex = 2;                          // "Duplicates"
        Assert.Equal(2, vm.VisibleProposals.Count());
        Assert.All(vm.VisibleProposals, p => Assert.Equal(ProposalKind.Dedup, p.Proposal.Kind));
        vm.SelectedTabIndex = 1;                          // "Migrations"
        Assert.Single(vm.VisibleProposals);
    }

    [Fact]
    public void Command_palette_filters_by_query_and_invokes()
    {
        var ran = "";
        var vm = new CommandPaletteViewModel(
        [
            new PaletteCommand("Go to Library", "Browse", () => ran = "library"),
            new PaletteCommand("Go to Health", "Problems", () => ran = "health"),
        ]);
        vm.Open();
        Assert.True(vm.IsOpen);
        Assert.Equal(2, vm.Results.Count);

        vm.Query = "health";
        Assert.Single(vm.Results);
        vm.InvokeCommand.Execute(vm.Results[0]);
        Assert.Equal("health", ran);   // the command actually ran (was dead before E9)
        Assert.False(vm.IsOpen);        // …and the palette closed
    }

    [Fact]
    public void DupeReview_user_can_choose_which_copy_survives()
    {
        var vm = new DupeReviewViewModel(new StubReclaim());
        var group = new DuplicateGroup("ID.PKG.1", "sig",
        [
            new DuplicateCopy(10, 1, "a.var", true, 100),
            new DuplicateCopy(20, 3, "b.var", true, 100),
        ]);
        vm.SetGroup(group);
        Assert.Equal(10, vm.KeepId);          // default keep = first copy

        vm.SelectedCopy = vm.Copies[1];        // user picks the second copy
        Assert.Equal(20, vm.KeepId);           // …so the second one survives (was impossible before A11)
    }
}
