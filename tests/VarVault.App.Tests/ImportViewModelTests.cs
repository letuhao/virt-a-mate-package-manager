using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Slice D (doc 31 Phase 6.1/6.6): the Import view-model scans real sources into lanes, gates Apply until
/// every review item is decided, accepts recommendations in one click, and applies into the target repo.</summary>
public sealed class ImportViewModelTests
{
    [Fact]
    public async Task Scan_gate_acceptAll_apply_over_real_services()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var catalogDir = new TempDirectory();
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        WriteVar(catalogDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "CCC"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("catalog", catalogDir.Path));
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", ("Custom/n.vam", "N"));                       // New
        WriteVar(importDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "CCC"), ("Custom/b2.vam", "X")); // Conflict

        using var read = host.Host.Services.CreateScope();
        var vm = new ImportViewModel(
            read.ServiceProvider.GetRequiredService<IImportService>(),
            read.ServiceProvider.GetRequiredService<IRepositoryService>());
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.TotalScanned >= 2);
        Assert.Equal(1, vm.ConflictCount);
        Assert.True(vm.ReviewRemaining >= 1);
        Assert.False(vm.CanApply);                 // gated until reviewed

        vm.AcceptAllCommand.Execute(null);
        Assert.Equal(0, vm.ReviewRemaining);
        Assert.True(vm.CanApply);                  // now unblocked

        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Fresh.New.1.var")));   // New imported
        Assert.Contains("copied", vm.StatusMessage ?? "");
        Assert.Single(vm.History);                 // run recorded
    }

    [Fact]
    public async Task Keyboard_navigates_and_decides_review_items()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var catalogDir = new TempDirectory();
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        WriteVar(catalogDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "CCC"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("catalog", catalogDir.Path));
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        WriteVar(importDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "CCC"), ("Custom/b2.vam", "X")); // Conflict

        using var read = host.Host.Services.CreateScope();
        var vm = new ImportViewModel(
            read.ServiceProvider.GetRequiredService<IImportService>(),
            read.ServiceProvider.GetRequiredService<IRepositoryService>());
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ConflictCount);
        Assert.False(vm.CanApply);

        // Land on the conflict item and resolve it via the keyboard map (] = keep-incoming).
        vm.MoveSelection(+1);
        vm.MoveSelection(-1);                       // clamps, stays in-range
        vm.Selected = vm.Items.First(i => i.Lane == ImportLane.Conflict);
        Assert.False(vm.Selected!.IsResolved);

        vm.DecidePrimary();                          // "]" → KeepIncoming
        Assert.Equal(ImportDecision.KeepIncoming, vm.Selected.Decision);
        Assert.Equal(0, vm.ReviewRemaining);
        Assert.True(vm.CanApply);

        vm.DiscardSelected();                         // "Del" re-decides the selected review item
        Assert.Equal(ImportDecision.Discard, vm.Selected.Decision);
    }

    [Fact]
    public async Task Lane_flags_apply_plan_and_search_filter()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var catalogDir = new TempDirectory();
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        WriteVar(catalogDir.Path, "Dup.Pack.1.var", "Dup", "Pack", ("Custom/d.vam", "D"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("catalog", catalogDir.Path));
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", ("Custom/n.vam", "N"));      // New
        WriteVar(importDir.Path, "Dup.Pack.1.var", "Dup", "Pack", ("Custom/d.vam", "D"));        // Exact dup

        using var read = host.Host.Services.CreateScope();
        var vm = new ImportViewModel(
            read.ServiceProvider.GetRequiredService<IImportService>(),
            read.ServiceProvider.GetRequiredService<IRepositoryService>());
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);
        await vm.ScanCommand.ExecuteAsync(null);

        var fresh = vm.Items.First(i => i.FileName == "Fresh.New.1.var");
        Assert.True(fresh.IsNew);
        Assert.False(fresh.NeedsReview);
        Assert.Equal("New", fresh.ListPillText);                        // auto lane → lane label

        // Apply-plan summary reflects the auto decisions (New→Import copy, Exact→Skip).
        Assert.Contains("Copy 1", vm.ApplyPlanSummary);
        Assert.Contains("skip 1", vm.ApplyPlanSummary);

        // Search filters the visible list.
        vm.SearchText = "Fresh";
        Assert.Single(vm.Items);
        Assert.Equal("Fresh.New.1.var", vm.Items[0].FileName);
        vm.SearchText = "";
        Assert.True(vm.Items.Count >= 2);
    }

    private static void WriteVar(string dir, string fileName, string creator, string package, params (string, string)[] entries)
    {
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Write(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{}}");
        foreach (var (n, c) in entries)
            Write(zip, n, c);
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
