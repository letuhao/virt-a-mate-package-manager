using System.Linq;
using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GD-13..17 · Screen logic wired to real service data: proposals select/approve/reject, analytics bar
/// fractions, trash tab switching + backups, activity filtering, settings dropdowns. (18-gap G-D.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class GdScreenLogicTests
{
    // GD-13
    [Fact]
    public async Task Proposals_approve_selected_only_approves_checked_and_reject_all_clears()
    {
        var pending = new List<Proposal>
        {
            new("1", ProposalKind.Rebalance, "Rebalance", "d", 1L << 30, []),
            new("2", ProposalKind.Dedup, "Dedup", "d", 2L << 30, []),
        };
        var svc = new StubProposals(pending);
        var vm = new ProposalsViewModel(svc, new FakeDialogLauncher());
        await vm.LoadAsync();
        Assert.Equal(2, vm.PendingCount);
        Assert.Equal("⇄", vm.Pending[0].Icon);       // kind → glyph
        Assert.Equal("verified", vm.Pending[1].Tag);  // dedup → verified

        vm.Pending[0].IsSelected = true;
        await vm.ApproveSelectedCommand.ExecuteAsync(null);
        Assert.Single(svc.Approved);
        Assert.Equal(1, vm.PendingCount);

        await vm.RejectAllCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.PendingCount);
    }

    // GD-14
    [Fact]
    public async Task Analytics_rows_carry_bar_fractions_relative_to_max()
    {
        var vm = new AnalyticsViewModel(new StubAnalytics(
            byType: [new SpaceByGroup("Assets", 1000, 0), new SpaceByGroup("Morphs", 500, 0)]));
        await vm.RefreshAsync();
        Assert.Equal(1.0, vm.ByType[0].Fraction, 3);   // max
        Assert.Equal(0.5, vm.ByType[1].Fraction, 3);   // half
    }

    // GD-15
    [Fact]
    public async Task Trash_tabs_switch_and_backups_load()
    {
        var vm = new TrashViewModel(new StubTrash(
            items: [new TrashItemDto("t1", "/x", "dup", default, 100)],
            backups: [new BackupDto("catalog.bak", default, 10)]));
        await vm.LoadAsync();
        Assert.True(vm.IsTrashTab);
        Assert.False(vm.IsBackupsTab);
        Assert.Single(vm.Backups);
        vm.SelectedTabIndex = 1;
        Assert.True(vm.IsBackupsTab);
    }

    // GD-16
    [Fact]
    public async Task Activity_filter_narrows_the_log()
    {
        var vm = new ActivityViewModel(new StubActivityLog(
        [
            new VarVault.Sdk.Activation.ActivityRecord("migrate", "a", default),
            new VarVault.Sdk.Activation.ActivityRecord("delete", "b", default),
        ]));
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Items.Count);
        vm.SelectedFilter = "Deletes";
        Assert.Single(vm.Items);
        Assert.Equal("delete", vm.Items[0].Kind);
    }

    // GD-17
    [Fact]
    public async Task Settings_dropdowns_and_save_persist_all_fields()
    {
        var store = new StubSettings();
        var vm = new SettingsViewModel(store)
        {
            VamPath = "D:\\VaM",
            CatalogDbPath = "C:\\catalog.db",
            SymlinkType = "Directory-swap profiles (fast)",
            FixOnImport = "Auto (high-confidence)",
        };
        Assert.Equal(3, vm.FixOnImportOptions.Count);
        Assert.Equal(2, vm.SymlinkOptions.Count);
        await vm.SaveCommand.ExecuteAsync(null);

        var reloaded = new SettingsViewModel(store);
        await reloaded.LoadCommand.ExecuteAsync(null);
        Assert.Equal("D:\\VaM", reloaded.VamPath);
        Assert.Equal("C:\\catalog.db", reloaded.CatalogDbPath);
        Assert.Equal("Auto (high-confidence)", reloaded.FixOnImport);
    }
}
