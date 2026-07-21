using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Services;
using VarVault.Common;
using VarVault.Sdk.Import;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Background import jobs: enqueue is immediate, progress arrives, cancel propagates, scan installs a session.</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class ImportJobRunnerTests
{
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition(), "condition not met within timeout");
    }

    [Fact]
    public async Task Scan_job_enqueues_immediately_and_returns_a_session()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        ImportFixtures.WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", ("Custom/n.vam", "N"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<Sdk.Repositories.IRepositoryService>();
            targetId = (await repos.RegisterAsync(new Sdk.Repositories.RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }

        var runner = host.Host.Services.GetRequiredService<ImportJobRunner>();
        var queue = host.Host.Services.GetRequiredService<IJobQueue>();
        var job = runner.StartScan(new ImportSpec([importDir.Path], targetId));

        Assert.Same(job.Handle, queue.Active.FirstOrDefault());
        var session = await job.Result;
        Assert.True(session.Items.Count >= 1);
        await Until(() => queue.Active.Count == 0);
    }

    [Fact]
    public async Task Scan_job_reports_progress_while_running()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        for (var i = 0; i < 5; i++)
            ImportFixtures.WriteVar(importDir.Path, $"Fresh.New{i}.1.var", "Fresh", $"New{i}", ($"Custom/n{i}.vam", "N"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<Sdk.Repositories.IRepositoryService>();
            targetId = (await repos.RegisterAsync(new Sdk.Repositories.RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }

        var runner = host.Host.Services.GetRequiredService<ImportJobRunner>();
        var job = runner.StartScan(new ImportSpec([importDir.Path], targetId));

        var sawProgress = false;
        for (var i = 0; i < 300; i++)
        {
            if (job.Handle.Progress.Total > 0 || !string.IsNullOrEmpty(job.Handle.Progress.Message))
                sawProgress = true;
            if (job.Handle.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
                break;
            await Task.Delay(10);
        }

        await job.Result;
        Assert.True(sawProgress);
    }

    [Fact]
    public async Task Cancel_on_scan_job_propagates_cancellation()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        ImportFixtures.WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", ("Custom/n.vam", "N"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<Sdk.Repositories.IRepositoryService>();
            targetId = (await repos.RegisterAsync(new Sdk.Repositories.RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }

        var runner = host.Host.Services.GetRequiredService<ImportJobRunner>();
        var job = runner.StartScan(new ImportSpec([importDir.Path], targetId));
        await Until(() => job.Handle.State == JobState.Running);
        job.Handle.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => job.Result);
        Assert.Equal(JobState.Cancelled, job.Handle.State);
    }

    [Fact]
    public void FreezeSession_captures_decisions_independently_of_live_items()
    {
        var item = new ImportItem(
            Guid.NewGuid(), "src", "/src", "A.B.1.var", "/src/A.B.1.var", "a.b",
            ImportLane.Conflict, new ImportSignals(true, "ok", 1, 1, DateTime.UtcNow, false,
                null, "A.B", null, false, null, 0),
            ImportDecision.KeepExisting, "conflict", null, []);
        item.Decision = ImportDecision.KeepIncoming;
        var session = new ImportSession(Guid.NewGuid(), "/tmp", Guid.NewGuid(), false, [], [item], []);

        var frozen = ImportJobRunner.FreezeSession(session, [(item.Id, ImportDecision.Discard)]);
        Assert.Equal(ImportDecision.Discard, frozen.Items[0].Decision);
        Assert.Equal(ImportDecision.KeepIncoming, item.Decision);
        Assert.NotSame(item, frozen.Items[0]);
    }
}

internal static class ImportFixtures
{
    public static void WriteVar(string dir, string fileName, string creator, string package, params (string, string)[] entries)
    {
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        Write(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{}}");
        foreach (var (n, c) in entries)
            Write(zip, n, c);
    }

    private static void Write(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open();
        s.Write(System.Text.Encoding.UTF8.GetBytes(content));
    }
}
