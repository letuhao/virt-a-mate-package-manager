using VarVault.Sdk.Activation;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-9 · Rescue applies the baseline via IActivationService. (16-checklist DLG-9.)</summary>
[Trait("Category", TestCategories.Unit)]
public class RescueDialogTests
{
    private sealed class StubActivation : IActivationService
    {
        public Task<int> RescueAsync(long profileId, CancellationToken ct = default) => Task.FromResult(1847);
        public Task<ActivationBuildResult> BuildProfileLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ActivationBuildResult> DeactivateAsync(long p, long pkg, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CleanTempLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ReconcileProfilesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    [Fact]
    public async Task Apply_calls_rescue()
    {
        var vm = new RescueViewModel(new StubActivation());
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Contains("Deactivated 1847", vm.ResultMessage);
    }
}
