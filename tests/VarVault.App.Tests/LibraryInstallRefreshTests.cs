using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
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
        var presets = new FakePresetService();
        var activation = new FakeActivationService(query);
        var vm = new LibraryViewModel(query, presets: presets, activation: activation);

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

    private sealed class FakeActivationService(InstallStateLibraryQuery query) : IActivationService
    {
        public Task<ActivationBuildResult> BuildProfileLinksAsync(long presetId, CancellationToken cancellationToken = default)
        {
            query.MarkInstalled();
            return Task.FromResult(new ActivationBuildResult(1, 0, 0));
        }

        public Task<ActivationBuildResult> DeactivateAsync(long presetId, long packageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> RescueAsync(long profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<int>> RescueActiveAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReconcileProfilesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CleanTempLinksAsync(long profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakePresetService : IPresetService
    {
        private const long PresetId = 42;

        public Task<Result<PresetInfo>> CreateAsync(string name, IEnumerable<string> memberRefs, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new PresetInfo(PresetId, name, 0)));

        public Task<IReadOnlyList<PresetInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PresetInfo>>([new PresetInfo(PresetId, "Library installs", 0)]);

        public Task<bool> DeleteAsync(long presetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<Result<PresetInfo>> AddMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new PresetInfo(presetId, "Library installs", 1)));

        public Task<Result<PresetInfo>> RemoveMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success(new PresetInfo(presetId, "Library installs", 0)));

        public Task<IReadOnlyList<string>> MembersAsync(long presetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<ActivationPreview?> PreviewActivationAsync(long presetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ActivationPreview?>(null);

        public Task RefreshMemberResolutionsAsync(long presetId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
