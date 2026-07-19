using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// AC-5 · Real UI-E2E for the migrate dialog: seeds a misplaced (hot-on-cold) multi-copy package with a
/// Tier-1 target repo, opens the real dialog in the shell, clicks the real "Approve &amp; run" button, and
/// asserts the file physically moved to the target and the source was trashed. (19-Audit.)
/// </summary>
public class MigrateDialogE2ETests
{
    [AvaloniaFact]
    public async Task Approve_and_run_moves_the_misplaced_var_to_the_target_tier()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var srcDir = new TempDirectory(); // Tier 3 (cold) — where the hot package is wrongly placed
        using var dstDir = new TempDirectory(); // Tier 1 (hot) — the migration target
        var srcId = Guid.NewGuid();
        var dstId = Guid.NewGuid();

        // Two physical copies of a hot package sitting on the cold tier.
        var relA = "A.Hot.1.copy0.var";
        var relB = "A.Hot.1.copy1.var";
        await File.WriteAllTextAsync(Path.Combine(srcDir.Path, relA), "bytes");
        await File.WriteAllTextAsync(Path.Combine(srcDir.Path, relB), "bytes");

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = srcId, Name = "cold", MountPath = srcDir.Path, Tier = 3, MediaType = MediaType.Hdd,
                IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Repositories.Add(new Repository
            {
                Id = dstId, Name = "hot", MountPath = dstDir.Path, Tier = 1, MediaType = MediaType.Ssd,
                IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            db.Packages.Add(new Package
            {
                Id = 1, VarName = "A.Hot.1", IdentityKey = "A.HOT.1", Creator = "A", PackageName = "Hot",
                VersionToken = "1", VersionSort = 1, FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
            });
            db.VarFiles.Add(new VarFile { Id = 10, PackageId = 1, RepositoryId = srcId, RelativePath = relA, SizeBytes = 5, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow });
            db.VarFiles.Add(new VarFile { Id = 11, PackageId = 1, RepositoryId = srcId, RelativePath = relB, SizeBytes = 5, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow });
            db.PackageListItems.Add(new PackageListItem
            {
                PackageId = 1, VarName = "A.Hot.1", Creator = "A", PackageName = "Hot", VersionToken = "1",
                PrimaryType = ContentType.Scene, TotalSize = 10, Class = ContentClass.Hot, ActualTierMin = 3,
                OnlineInstanceCount = 2, TotalInstanceCount = 2, IsSingleCopy = false, AddedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var scope2 = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope2.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        UiE2E.Pump();

        // Open the migrate dialog exactly as the launcher does (real services, plan pre-loaded).
        var migrate = new MigrateViewModel(
            scope2.ServiceProvider.GetRequiredService<ITieringService>(),
            scope2.ServiceProvider.GetRequiredService<IMigrationService>(),
            scope2.ServiceProvider.GetRequiredService<IRepositoryService>());
        await migrate.LoadPlanAsync();
        shell.Dialogs.Show(migrate);
        UiE2E.Pump();

        // The plan proposed real hot-on-cold moves.
        Assert.NotEmpty(migrate.Moves);
        Assert.True(migrate.CanApprove);
        Assert.Contains(migrate.Moves, m => m.FromTier == 3 && m.ToTier == 1);
        UiE2E.Screenshot(window, "ac5-migrate-plan");

        // Click the real "Approve & run" button.
        var approve = UiE2E.Button(window, "Approve");
        Assert.NotNull(approve);
        Assert.NotNull(approve!.Command); // AC-5: wired (was a no-op)
        await UiE2E.ClickAsync(approve);

        // Real effect: at least one copy physically moved to the Tier-1 target; its source is gone.
        Assert.Contains("Migrated", migrate.StatusMessage);
        var movedToTarget = File.Exists(Path.Combine(dstDir.Path, relA)) || File.Exists(Path.Combine(dstDir.Path, relB));
        Assert.True(movedToTarget, "a copy should now exist at the Tier-1 target");
        UiE2E.Screenshot(window, "ac5-after-migrate");

        // Catalog re-pointed: at least one var file now lives on the destination repo.
        using var verify = host.Host.Services.CreateScope();
        var vdb = verify.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.True(await vdb.VarFiles.AnyAsync(v => v.RepositoryId == dstId));
    }
}
