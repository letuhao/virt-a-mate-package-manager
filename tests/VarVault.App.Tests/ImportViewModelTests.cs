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
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
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
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);

        await vm.ScanCommand.ExecuteAsync(null);
        Assert.True(vm.TotalScanned >= 2);
        Assert.Equal(1, vm.ConflictCount);
        Assert.True(vm.ReviewRemaining >= 1);
        Assert.True(vm.CanApply);                  // Apply allowed; undecided → skip at apply
        Assert.True(vm.HasUndecidedReview);

        vm.AcceptAllCommand.Execute(null);
        Assert.Equal(0, vm.ReviewRemaining);
        Assert.True(vm.CanApply);

        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Fresh.New.1.var")));   // New imported
        Assert.Contains("copied", vm.StatusMessage ?? "");
        Assert.Single(vm.History);                 // run recorded

        // History detail is a reviewable, paged per-item ledger — not summary counts only.
        await vm.OpenHistoryCommand.ExecuteAsync(null);
        Assert.True(vm.HasHistoryDetail);
        Assert.NotNull(vm.SelectedHistoryRun);
        Assert.Equal(vm.SelectedHistoryRun!.Outcomes.Count, vm.HistoryOutcomesPager.TotalCount);
        Assert.Equal(25, vm.HistoryOutcomesPager.PageSize);
        Assert.Equal(vm.SelectedHistoryRun.Outcomes.Count, vm.HistoryOutcomesPager.Items.Count);

        await vm.FilterHistoryOutcomesCommand.ExecuteAsync("failed");
        Assert.Equal(vm.SelectedHistoryRun.Outcomes.Count(o => !o.Ok), vm.HistoryOutcomesPager.TotalCount);
    }

    [Fact]
    public async Task Stale_scan_result_is_rejected_and_temp_is_cleaned()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();
        using var otherDir = new TempDirectory();

        WriteVar(importDir.Path, "Fresh.New.1.var", "Fresh", "New", ("Custom/n.vam", "N"));
        WriteVar(otherDir.Path, "Other.Pack.1.var", "Other", "Pack", ("Custom/o.vam", "O"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
            targetId = (await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;

        using var read = host.Host.Services.CreateScope();
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);

        // Start scan, then mutate sources after the request token is captured — stale guard must reject + clean temp.
        var scan = vm.ScanCommand.ExecuteAsync(null);
        for (var i = 0; i < 200 && !vm.IsScanning; i++)
            await Task.Delay(10);
        Assert.True(vm.IsScanning);
        vm.AddSourcePath(otherDir.Path);
        await scan;

        Assert.False(vm.HasSession);
        Assert.Contains("inputs changed", vm.StatusMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Keyboard_navigates_and_decides_review_items()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
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
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ConflictCount);
        Assert.True(vm.CanApply);                   // not gated on full review anymore
        Assert.True(vm.HasUndecidedReview);

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
    public async Task Apply_skips_undecided_review_items_and_keeps_explicit_choices()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var catalogDir = new TempDirectory();
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        WriteVar(catalogDir.Path, "Creator.PackA.1.var", "Creator", "PackA", ("Custom/a.vam", "AAA"));
        WriteVar(catalogDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "BBB"));

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("catalog", catalogDir.Path));
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        // Two conflicts + one fresh new.
        WriteVar(importDir.Path, "Creator.PackA.1.var", "Creator", "PackA", ("Custom/a.vam", "AAA"), ("Custom/a2.vam", "X"));
        WriteVar(importDir.Path, "Creator.PackB.1.var", "Creator", "PackB", ("Custom/b.vam", "BBB"), ("Custom/b2.vam", "Y"));
        WriteVar(importDir.Path, "Fresh.Only.1.var", "Fresh", "Only", ("Custom/n.vam", "N"));

        using var read = host.Host.Services.CreateScope();
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);
        await vm.ScanCommand.ExecuteAsync(null);

        Assert.True(vm.ConflictCount >= 2, $"expected ≥2 conflicts, got {vm.ConflictCount}; lanes={string.Join(',', vm.Items.Select(i => $"{i.FileName}:{i.Lane}"))}");
        Assert.True(vm.CanApply);

        var keep = vm.Items.First(i => i.Lane == ImportLane.Conflict);
        var otherConflict = vm.Items.First(i => i.Lane == ImportLane.Conflict && i.Model.Id != keep.Model.Id);
        keep.Decision = ImportDecision.KeepIncoming;
        // Leave otherConflict undecided — Apply must treat it as KeepExisting (skip incoming).

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(targetDir.Path, keep.FileName))); // kept new
        Assert.True(File.Exists(Path.Combine(targetDir.Path, "Fresh.Only.1.var"))); // auto New
        Assert.False(File.Exists(Path.Combine(targetDir.Path, otherConflict.FileName))); // undecided → skipped
    }

    [Fact]
    public async Task Lane_flags_apply_plan_and_search_filter()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
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
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
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

    [Fact]
    public async Task Pending_sources_show_before_scan_and_gate_hint_explains_disabled_scan()
    {
        await using var host = TestHost.Create(withPersistence: true, configure: ImportTestHelpers.RegisterImportJobs);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IRepositoryService>()
                .RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path));

        using var read = host.Host.Services.CreateScope();
        var vm = ImportTestHelpers.CreateImportViewModel(read.ServiceProvider);
        await vm.LoadAsync();

        Assert.False(vm.HasPendingSources);
        Assert.False(vm.CanScan);
        Assert.Contains("folder or archive", vm.GateHint, StringComparison.OrdinalIgnoreCase);

        vm.AddSourcePath(importDir.Path);
        Assert.True(vm.HasPendingSources);
        Assert.True(vm.CanScan);
        Assert.Contains("Ready to Scan", vm.GateHint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 source", vm.SourcesSummary, StringComparison.OrdinalIgnoreCase);

        vm.RemoveSourceCommand.Execute(importDir.Path);
        Assert.False(vm.HasPendingSources);
        Assert.False(vm.CanScan);
        Assert.Empty(vm.SourcePaths);
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
