using System.Linq;
using VarVault.TestKit;
using VarVault.App.ViewModels;

namespace VarVault.App.Tests;

/// <summary>
/// GC-2 · Each tabbed surface exposes its exact prototype tab labels and a two-way selected index.
/// (18-gap GC-2.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class TabAdoptionTests
{
    [Fact]
    public void Tiering_has_its_four_tabs()
    {
        var vm = new TieringViewModel(new StubTiering());
        Assert.Equal(
            new[] { "Overview", "Lifecycle rules", "Placement policy", "Stale / old versions" },
            vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Dupes_has_its_three_tabs()
    {
        // "Download intake" was retired — the Import screen replaces it (doc 30 §10).
        var vm = new DupesViewModel(new StubReclaim());
        Assert.Equal(new[] { "Reclaim space", "Exact duplicates", "Near-duplicates" },
            vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Health_has_its_four_tabs()
    {
        var vm = new HealthViewModel(new StubHealth());
        Assert.Equal(
            new[] { "Encoding", "Integrity / corrupt", "Missing meta", "VaM load" },
            vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Proposals_has_its_five_tabs()
    {
        var vm = new ProposalsViewModel(new StubProposals());
        Assert.Equal(new[] { "All", "Migrations", "Duplicates", "Encoding fixes", "Stale" },
            vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Trash_has_its_two_tabs()
    {
        var vm = new TrashViewModel(new StubTrash());
        Assert.Equal(new[] { "Trash (recoverable)", "Catalog backups" }, vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Settings_has_its_five_tabs()
    {
        var vm = new SettingsViewModel(new StubSettings());
        Assert.Equal(new[] { "General", "Tiers & policy", "Automation", "Import", "Advanced" },
            vm.Tabs.Select(t => t.Label));
    }

    [Fact]
    public void Selecting_a_tab_updates_the_index()
    {
        var vm = new HealthViewModel(new StubHealth());
        Assert.Equal(0, vm.SelectedTabIndex);
        vm.SelectedTabIndex = 2;
        Assert.Equal(2, vm.SelectedTabIndex);
    }
}
