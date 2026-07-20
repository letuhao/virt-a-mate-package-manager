using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GA-5 · The GUI's startup trigger enqueues a real index run on the job queue (BE-N0), so a fresh launch
/// actually populates the catalog — the piece the audit found missing. (18-gap GA-5.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class StartupIndexTests
{
    private static async Task Until(Func<bool> cond, int maxMs = 180_000)
    {
        for (var i = 0; i < maxMs / 25 && !cond(); i++)
            await Task.Delay(25);
        Assert.True(cond(), "condition not met within timeout");
    }

    [Fact]
    public async Task EnqueueIndexAll_returns_a_job_when_indexing_is_composed()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var handle = AppHost.EnqueueIndexAll(scope.ServiceProvider);
        Assert.NotNull(handle);
        Assert.Contains("Index", handle!.Name, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Startup_index_over_a_real_repo_populates_the_library()
    {
        Skip.If(TestCorpus.Primary is null, "requires VARVAULT_TEST_CORPUS");
        var repoPath = TestCorpus.Primary!;

        await using var host = TestHost.Create(withPersistence: true);
        var scope = host.Host.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var repos = sp.GetRequiredService<IRepositoryService>();
        var reg = await repos.RegisterAsync(new RegisterRepositoryRequest("real", repoPath));
        Assert.True(reg.IsSuccess);

        // Drive exactly what the shell does at launch: enqueue the index job.
        var handle = AppHost.EnqueueIndexAll(sp);
        Assert.NotNull(handle);
        await Until(() => handle!.State is JobState.Completed or JobState.Failed);
        Assert.True(handle!.State == JobState.Completed, $"index job failed: {handle.Error?.Message}");

        // The library read model is now non-empty — proves the GUI trigger indexed the real corpus.
        var library = sp.GetRequiredService<ILibraryQueryService>();
        var page = await library.GetPageAsync(new LibraryQuery(0, 50));
        Assert.True(page.TotalCount > 0, $"expected indexed packages from {repoPath}");
    }
}
