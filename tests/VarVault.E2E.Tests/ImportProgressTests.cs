using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Sdk.Import;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;
using static VarVault.E2E.Tests.ImportFixtures;

namespace VarVault.E2E.Tests;

/// <summary>Import Scan/Apply phase progress, temp cleanup on scan failure, and apply progress reporting.</summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ImportProgressTests
{
    private sealed class CapturingProgress : IProgressSink
    {
        public List<ProgressReport> Reports { get; } = [];
        public void Report(ProgressReport report) { lock (Reports) Reports.Add(report); }
    }

    [Fact]
    public async Task Scan_reports_preparation_catalog_and_classification_phases()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", [("Custom/n.vam", "NEW")]);

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
            targetId = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;

        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();
        var progress = new CapturingProgress();
        _ = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId), progress);

        List<ProgressReport> reports;
        lock (progress.Reports) reports = [.. progress.Reports];
        Assert.Contains(reports, r => r.Message?.Contains("Preparing", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Loading catalog", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Classifying", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Scan complete", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Cancelled_scan_cleans_temp_workspace()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", [("Custom/n.vam", "NEW")]);

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
            targetId = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;

        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.ScanAsync(new ImportSpec([importDir.Path], targetId), progress: null!, cts.Token));
    }

    [Fact]
    public async Task Apply_reports_preflight_copy_and_history_phases()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", [("Custom/n.vam", "NEW")]);

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
            await scope.ServiceProvider.GetRequiredService<Sdk.Indexing.IIndexOrchestrator>().IndexAllAsync();
        }

        using var read = host.Host.Services.CreateScope();
        var svc = read.ServiceProvider.GetRequiredService<IImportService>();
        var session = await svc.ScanAsync(new ImportSpec([importDir.Path], targetId));
        var progress = new CapturingProgress();
        _ = await svc.ApplyAsync(session, progress);

        List<ProgressReport> reports;
        lock (progress.Reports) reports = [.. progress.Reports];
        Assert.Contains(reports, r => r.Message?.Contains("Preflight", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Applying", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Recording history", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(reports, r => r.Message?.Contains("Cleaning up", StringComparison.OrdinalIgnoreCase) == true);
    }
}
