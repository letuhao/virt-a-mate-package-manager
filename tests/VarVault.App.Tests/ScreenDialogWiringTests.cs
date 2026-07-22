using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GD/GE reachability · Each screen's action command actually opens its dialog through the launcher
/// (HR-G0 "wired, not drawn"; HR-G1 reachability). (18-gap G-D/G-E.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ScreenDialogWiringTests
{
    [Fact]
    public void Repositories_add_and_rebalance_open_dialogs()
    {
        var l = new FakeDialogLauncher();
        var vm = new RepositoriesViewModel(new StubReposEmpty(), l);
        vm.AddRepoCommand.Execute(null);
        vm.RebalanceCommand.Execute(null);
        Assert.Contains("add-repo", l.Opened);
        Assert.Contains("migrate", l.Opened);
    }

    [Fact]
    public void Tiering_plan_opens_migrate_dialog()
    {
        var l = new FakeDialogLauncher();
        var vm = new TieringViewModel(new StubTiering(), l);
        vm.PlanCommand.Execute(null);
        Assert.Contains("migrate", l.Opened);
    }

    [Fact]
    public void Dupes_review_opens_dupe_dialog()
    {
        var l = new FakeDialogLauncher();
        var vm = new DupesViewModel(new StubReclaim(), l);
        var group = new DuplicateGroup("Creator.Pkg.1", "sig", []);
        vm.ReviewCommand.Execute(group);
        Assert.Contains("dupe:Creator.Pkg.1", l.Opened);
    }

    [Fact]
    public void Health_fix_opens_fix_dialog()
    {
        var l = new FakeDialogLauncher();
        var vm = new HealthViewModel(new StubHealth(), l);
        vm.FixGroupCommand.Execute(new EncodingGroup("GBK", 431));
        vm.FixAllCommand.Execute(null);
        Assert.Contains("fix:GBK", l.Opened);
        Assert.Contains("fix:", l.Opened);
    }

    [Fact]
    public void Missing_resolve_opens_manage_aliases_dialog()
    {
        var l = new FakeDialogLauncher();
        var vm = new MissingDepsViewModel(new StubMissing(), l);
        vm.ResolveCommand.Execute(new MissingDependency("Gone.Deleted.1", 83));
        Assert.Contains("manage-aliases:Gone.Deleted.1", l.Opened);
    }

    [Fact]
    public void Missing_manage_aliases_opens_dialog()
    {
        var l = new FakeDialogLauncher();
        var vm = new MissingDepsViewModel(new StubMissing(), l);
        vm.ManageAliasesCommand.Execute(null);
        Assert.Contains("manage-aliases", l.Opened);
    }

    [Fact]
    public void Presets_new_edit_deactivate_open_dialogs()
    {
        var l = new FakeDialogLauncher();
        var vm = new PresetsViewModel(new StubPresetsMin(), launcher: l) { Selected = new Sdk.Presets.PresetInfo(7, "Cinematic", 241) };
        vm.NewPresetCommand.Execute(null);
        vm.EditCommand.Execute(null);
        vm.DeactivateAllCommand.Execute(null);
        Assert.Contains("preset:0", l.Opened);
        Assert.Contains("preset:7", l.Opened);
        Assert.Contains("rescue", l.Opened);
    }

    [Fact]
    public void Dashboard_wizard_rescue_and_go_are_wired()
    {
        var l = new FakeDialogLauncher();
        var vm = new DashboardViewModel(new StubDashboard(), l);
        string? navigated = null;
        vm.NavigateTo = id => navigated = id;
        vm.SetupWizardCommand.Execute(null);
        vm.RescueCommand.Execute(null);
        vm.GoCommand.Execute("proposals");
        Assert.Contains("onboarding", l.Opened);
        Assert.Contains("rescue", l.Opened);
        Assert.Equal("proposals", navigated);
    }

    [Fact]
    public void Library_detail_opens_var_detail()
    {
        var l = new FakeDialogLauncher();
        var vm = new LibraryViewModel(new StubLibraryQuery(), launcher: l);
        var entry = new PackageListEntry(42, "Creator.Pkg.1", "Creator", "Pkg", "1", "look", 100, 1, 1, true, false, "hot", false, null);
        vm.OpenDetailCommand.Execute(entry);
        Assert.Contains("var-detail:42", l.Opened);
    }
}
