using VarVault.Common;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-9 · Rescue applies the baseline via IActivationService. (16-checklist DLG-9.)</summary>
[Trait("Category", TestCategories.Unit)]
public class RescueDialogTests
{
    private sealed class StubActivation : IActivationService
    {
        public long? LastBuildPresetId { get; private set; }
        public Task<int> RescueAsync(long profileId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Result<int>> RescueActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Result.Success(1847));
        public Task<ActivationBuildResult> BuildProfileLinksAsync(long id, CancellationToken ct = default)
        {
            LastBuildPresetId = id;
            return Task.FromResult(new ActivationBuildResult(3, 0, 0));
        }
        public Task<ActivationBuildResult> DeactivateAsync(long p, long pkg, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CleanTempLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ReconcileProfilesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubProfiles : IProfileService
    {
        public string? LastSwitched { get; private set; }
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> ActiveAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<Result> CreateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> SwitchToAsync(string profileName, CancellationToken ct = default)
        {
            LastSwitched = profileName;
            return Task.FromResult(Result.Success());
        }
        public Task<Result> DeleteAsync(string profileName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> RenameAsync(string oldName, string newName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Apply_calls_rescue_active_without_baseline()
    {
        var stub = new StubActivation();
        var vm = new RescueViewModel(stub);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Contains("Deactivated 1847", vm.ResultMessage);
        Assert.Contains("no baseline selected", vm.ResultMessage);
        Assert.Null(stub.LastBuildPresetId);
    }

    [Fact]
    public async Task Apply_activates_and_switches_selected_baseline_after_rescue()
    {
        var stub = new StubActivation();
        var profiles = new StubProfiles();
        var vm = new RescueViewModel(stub, profiles: profiles)
        {
            SelectedBaseline = new Sdk.Presets.PresetInfo(42, "Baseline", 1),
        };
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(42, stub.LastBuildPresetId);
        Assert.Equal("Baseline", profiles.LastSwitched);
        Assert.Contains("activated & switched to 'Baseline'", vm.ResultMessage);
        Assert.Contains("3 linked", vm.ResultMessage);
    }

    [Fact]
    public async Task Apply_surfaces_rescue_skip_without_claiming_deactivate()
    {
        var stub = new SkipActivation();
        var vm = new RescueViewModel(stub);
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Contains("rescue skipped", vm.ResultMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Deactivated", vm.ResultMessage);
    }

    private sealed class SkipActivation : IActivationService
    {
        public Task<int> RescueAsync(long profileId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<Result<int>> RescueActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(Result.Failure<int>("rescue.novamroot", "VaM install path is not set — rescue skipped."));
        public Task<ActivationBuildResult> BuildProfileLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ActivationBuildResult> DeactivateAsync(long p, long pkg, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CleanTempLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ReconcileProfilesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
