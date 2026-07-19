using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Real-repo UI-E2E over the actual `.var` corpus (NOT seeded rows): registers the real repositories, runs the
/// real indexing/extraction pipeline, drives the real shell to browse the indexed library, performs a real
/// cross-drive move (D:→E:), and records indexing performance. Non-destructive: indexing is read-only into a
/// temp catalog, and mutations only touch a scratch `.var` this test creates (cleaned up in finally). Skips
/// cleanly when the drives are absent. (19-Audit, real-repo feedback.)
/// </summary>
public class RealRepoE2ETests
{
    private const string Corpus = @"D:\VarVault_test_repo";
    private const string MoveTarget = @"E:\VarVault_test_repo_01";

    [AvaloniaFact]
    public async Task Index_real_corpus_browse_and_cross_drive_move()
    {
        if (!Directory.Exists(Corpus))
            return; // environment without the real corpus — skip

        // A scratch var we own, placed under the corpus repo, so mutations never touch the real corpus.
        var scratchDir = Path.Combine(Corpus, "__vv_ui_e2e__");
        var scratchRel = Path.Combine("__vv_ui_e2e__", "UiE2E.Scratch.1.var");
        var scratchSrc = Path.Combine(Corpus, scratchRel);
        var scratchDst = MoveTarget is not null ? Path.Combine(MoveTarget, scratchRel) : null;
        Directory.CreateDirectory(scratchDir);
        WriteMinimalVar(scratchSrc, "UiE2E", "Scratch");

        await using var host = TestHost.Create(withPersistence: true);
        try
        {
            var haveTarget = Directory.Exists(MoveTarget);

            // 1) Repo management: register the real repositories (real drive profiling).
            Guid corpusId, targetId = Guid.Empty;
            using (var scope = host.Host.Services.CreateScope())
            {
                var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
                var reg = await repos.RegisterAsync(new RegisterRepositoryRequest("corpus", Corpus));
                Assert.True(reg.IsSuccess, reg.IsSuccess ? "" : reg.Error.ToString());
                corpusId = reg.Value.Id;
                Assert.True(reg.Value.IsOnline);
                if (haveTarget)
                {
                    var t = await repos.RegisterAsync(new RegisterRepositoryRequest("target", MoveTarget));
                    Assert.True(t.IsSuccess, t.IsSuccess ? "" : t.Error.ToString());
                    targetId = t.Value.Id;
                }
            }

            // 2) Extraction/performance: run the REAL indexing pipeline over the real .var files.
            IndexRunSummary summary;
            var sw = Stopwatch.StartNew();
            using (var scope = host.Host.Services.CreateScope())
            {
                var orchestrator = scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>();
                summary = await orchestrator.IndexAllAsync();
            }
            sw.Stop();

            // The corpus has ~277 var files; real extraction should index a substantial number of packages.
            Assert.True(summary.Indexed >= 100, $"indexed only {summary.Indexed}");
            // Performance guard: extracting the corpus completes in a sane wall-clock (very loose upper bound).
            Assert.True(sw.Elapsed.TotalSeconds < 180, $"indexing took {sw.Elapsed.TotalSeconds:F1}s");

            // 3) Browse: launch the real shell over the freshly-indexed catalog and page the real library.
            var scope2 = host.Host.Services.CreateScope();
            var shell = AppHost.CreateShell(scope2.ServiceProvider);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            shell.Navigate("library");
            UiE2E.Pump();
            var lib = (LibraryViewModel)shell.ActiveScreen!;
            await lib.RefreshAsync();
            UiE2E.Pump();
            Assert.True(lib.TotalCount >= 100, $"library shows {lib.TotalCount}");
            Assert.NotEmpty(lib.Items);          // real packages extracted from real .var files
            Assert.NotEmpty(lib.CreatorOptions); // real creators (incl. CJK) populate the facet combo
            UiE2E.Screenshot(window, "real-corpus-library");

            // 3b) Gallery / preview images: the indexer extracts sibling .jpg previews from inside the vars
            // (like old varManager). Count how many packages got a stored preview, then render the gallery.
            int withPreview;
            using (var scope = host.Host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
                withPreview = await db.PackageListItems.CountAsync(p => p.PreviewThumbRef != null);
            }
            await lib.ToggleViewModeCommand.ExecuteAsync(null); // switch to gallery view
            UiE2E.Pump();
            Assert.NotEmpty(lib.GalleryItems); // gallery cards exist for the real packages
            if (withPreview > 0)
            {
                // Wait for at least one extracted thumbnail to decode and land on a card.
                for (var i = 0; i < 40 && lib.GalleryItems.All(g => !g.HasThumbnail); i++)
                {
                    UiE2E.Pump();
                    await Task.Delay(50);
                }
                Assert.Contains(lib.GalleryItems, g => g.HasThumbnail); // a real extracted preview renders
            }
            UiE2E.Screenshot(window, "real-corpus-gallery");

            // 4) Var management — cross-drive move (D:→E:) of the scratch var via the real migration service.
            if (haveTarget)
            {
                long scratchVarId;
                using (var scope = host.Host.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
                    scratchVarId = await db.VarFiles
                        .Where(v => v.RelativePath.Contains("__vv_ui_e2e__"))
                        .Select(v => v.Id).FirstAsync();
                }
                using (var scope = host.Host.Services.CreateScope())
                {
                    var migration = scope.ServiceProvider.GetRequiredService<IMigrationService>();
                    var result = await migration.RunAsync([new MigrationRequest(scratchVarId, targetId)]);
                    Assert.Equal(1, result.Moved);
                }
                Assert.True(File.Exists(scratchDst!), "scratch var should now exist on the E: target");
                Assert.False(File.Exists(scratchSrc), "scratch var should have left the D: source");
            }
        }
        finally
        {
            // Non-destructive cleanup: remove only our scratch artifacts.
            TryDeleteDir(scratchDir);
            if (scratchDst is not null)
                TryDeleteDir(Path.GetDirectoryName(scratchDst)!);
        }
    }

    private static void WriteMinimalVar(string path, string creator, string pkg)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var meta = zip.CreateEntry("meta.json");
        using var s = meta.Open();
        s.Write(Encoding.UTF8.GetBytes($"{{\"creatorName\":\"{creator}\",\"packageName\":\"{pkg}\"}}"));
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
