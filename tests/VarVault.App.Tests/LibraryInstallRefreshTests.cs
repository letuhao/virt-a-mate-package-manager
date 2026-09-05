using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Library install/uninstall must refresh loaded rows so the green installed dot updates in place.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class LibraryInstallRefreshTests
{
    [Fact]
    public async Task Install_selected_refreshes_loaded_row_installed_state_without_full_refresh()
    {
        var query = new InstallStateLibraryQuery();
        var actions = new FakeActions(query);
        var vm = new LibraryViewModel(query, actions: actions);

        await vm.RefreshAsync();
        Assert.False(vm.Items[0].IsActive);
        var callsBeforeInstall = query.GetByIdsCallCount;

        vm.ToggleSelection(vm.Items[0]);
        await vm.InstallSelectedAsync();

        Assert.True(vm.Items[0].IsActive);
        Assert.NotNull(vm.Items[0].InstalledAt);
        Assert.Equal(1, vm.Items.Count);
        Assert.Equal(callsBeforeInstall + 1, query.GetByIdsCallCount);
    }

    private sealed class InstallStateLibraryQuery : ILibraryQueryService
    {
        private bool _installed;

        public int GetByIdsCallCount { get; private set; }

        public void MarkInstalled() => _installed = true;

        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryPage([Entry(installed: false)], 1));

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Creator"]);

        public Task<IReadOnlyList<CreatorCount>> GetCreatorCountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CreatorCount>>([new CreatorCount("Creator", 1)]);

        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>([1L]);

        public Task<IReadOnlyList<PackageListEntry>> GetByIdsAsync(
            IReadOnlyList<long> packageIds,
            CancellationToken cancellationToken = default)
        {
            GetByIdsCallCount++;
            var installed = _installed;
            var installedAt = installed ? DateTime.UtcNow : (DateTime?)null;
            return Task.FromResult<IReadOnlyList<PackageListEntry>>(
                packageIds.Select(_ => Entry(installed, installedAt)).ToList());
        }

        private static PackageListEntry Entry(bool installed, DateTime? installedAt = null) =>
            new(1, "Creator.Look.1", "Creator", "Look", "1", "Look", 1024,
                1, 1, true, false, "Cold", false, null, InstalledAt: installedAt, IsActive: installed);
    }

    private sealed class FakeActions(InstallStateLibraryQuery query) : ILibraryActionService
    {
        public Task<BulkActionResult> AddToPresetAsync(long presetId, IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExportTxtAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> varFileIds, string subfolder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TxtResolveResult> ResolveTxtAsync(string txt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<MissingLogActivation> InstallIntoActiveProfileAsync(
            IReadOnlyList<string> varNames, CancellationToken cancellationToken = default)
        {
            query.MarkInstalled();
            return Task.FromResult(new MissingLogActivation(varNames.Count, varNames.Count, 0, 0));
        }
    }
}
