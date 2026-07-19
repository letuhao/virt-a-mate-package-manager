using VarVault.Common;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-1 · Onboarding add-and-index goes through IOnboardingService. (16-checklist DLG-1.)</summary>
[Trait("Category", TestCategories.Unit)]
public class OnboardingDialogTests
{
    private sealed class StubOnboarding : IOnboardingService
    {
        public Task<Result<OnboardingResult>> AddAndIndexAsync(string name, string path, long? reserve = null, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(new OnboardingResult(
                new RepositoryInfo(Guid.NewGuid(), name, path, "Hdd", 3, true, true, 8000, 5000, null),
                new IndexRunSummary(1, 42, 0, 0, 40, 2, 5))));
    }

    [Fact]
    public async Task Add_and_index_reports_result_and_completes()
    {
        var vm = new OnboardingViewModel(new StubOnboarding()) { FolderPath = @"D:\vars" };
        await vm.AddAndIndexCommand.ExecuteAsync(null);
        Assert.Equal("Indexed 42 vars", vm.ResultMessage);
        Assert.Equal(OnboardingStep.Done, vm.Step);
    }
}
