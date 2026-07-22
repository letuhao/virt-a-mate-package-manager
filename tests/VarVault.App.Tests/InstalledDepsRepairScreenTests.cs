using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Installed Packages repair wiring on the Missing deps screen.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class InstalledDepsRepairScreenTests
{
    private sealed class StubMissing : IMissingDepsQuery
    {
        public Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MissingDependency>>([]);
        public Task<PageResult<MissingDependency>> GetPageAsync(PageRequest request, string? searchText = null, CancellationToken ct = default) =>
            Task.FromResult(new PageResult<MissingDependency>([], 0, 1, request.SafePageSize));
    }

    private sealed class StubRepair : IInstalledDepsRepair
    {
        public InstalledDepsAnalysis Analysis { get; set; } = new([], 0);
        public List<string> Activated { get; } = [];

        public Task<InstalledDepsAnalysis> AnalyzeAsync(CancellationToken ct = default) =>
            Task.FromResult(Analysis);

        public Task<MissingLogActivation> ActivateFoundAsync(IReadOnlyList<string> varNames, CancellationToken ct = default)
        {
            Activated.AddRange(varNames);
            return Task.FromResult(new MissingLogActivation(varNames.Count, varNames.Count, 0, 0));
        }

        public Task<MissingLogActivation> ActivateFromAnalysisAsync(InstalledDepsAnalysis analysis, CancellationToken ct = default)
        {
            var names = analysis.Entries
                .Where(e => e.InLibrary && e.Via is not InstalledDepsResolveVia.Alias && !string.IsNullOrWhiteSpace(e.ResolvedVarName))
                .Select(e => e.ResolvedVarName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ActivateFoundAsync(names, ct);
        }
    }

    [Fact]
    public async Task AnalyzeInstalled_activates_found_and_lists_leftovers()
    {
        var repair = new StubRepair
        {
            Analysis = new InstalledDepsAnalysis(
            [
                new("A.Base.1", true, "A.Base.1", InstalledDepsResolveVia.Exact, 1, NeedsAlias: false),
                new("Ghost.Missing.9", false, null, InstalledDepsResolveVia.None, 2, NeedsAlias: true),
                new("A.Pack.2", true, "A.Pack.1", InstalledDepsResolveVia.Closest, 1, NeedsAlias: true),
            ], ActivePackageCount: 3),
        };
        var launcher = new FakeDialogLauncher();
        var vm = new MissingDepsViewModel(new StubMissing(), launcher, installedRepair: repair);

        await vm.AnalyzeInstalledCommand.ExecuteAsync(null);

        Assert.Contains("A.Base.1", repair.Activated);
        Assert.Contains("A.Pack.1", repair.Activated);
        Assert.Equal(2, vm.InstalledLeftovers.Count);
        Assert.Contains(vm.InstalledLeftovers, e => e.Ref == "Ghost.Missing.9");
        Assert.Contains(vm.InstalledLeftovers, e => e.Ref == "A.Pack.2");
        Assert.Contains("Analyzed 3 installed", vm.InstalledStatus);

        vm.ResolveInstalledLeftoverCommand.Execute(vm.InstalledLeftovers.Single(e => e.Ref == "A.Pack.2"));
        Assert.Contains("alias:A.Pack.2:A.Pack.1", launcher.Opened);
    }

    [Fact]
    public async Task AnalyzeInstalled_empty_active_reports_message()
    {
        var repair = new StubRepair { Analysis = new InstalledDepsAnalysis([], 0) };
        var vm = new MissingDepsViewModel(new StubMissing(), installedRepair: repair);
        await vm.AnalyzeInstalledCommand.ExecuteAsync(null);
        Assert.Contains("No installed packages", vm.InstalledStatus);
        Assert.Empty(vm.InstalledLeftovers);
        Assert.Empty(repair.Activated);
    }
}
