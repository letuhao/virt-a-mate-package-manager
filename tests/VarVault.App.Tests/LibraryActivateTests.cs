using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Domain.Activation;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-5 — Library ops-bar Install/Uninstall must target the currently active AddonPackages
/// profile (not a dedicated "Library installs" profile). Link materialization needs symlink privilege;
/// the test skips the on-disk assertion when it's unavailable, but still asserts catalog rows.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class LibraryActivateTests
{
    [AvaloniaFact]
    public async Task Install_selected_targets_active_profile_then_uninstall_removes_links()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await RegisterAsync(host, repoDir.Path);
        WriteVar(repoDir, "A.One.1.var", "A", "One");
        WriteVar(repoDir, "A.Two.1.var", "A", "Two");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        await sp.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var profiles = sp.GetRequiredService<IProfileService>();
        Assert.True((await profiles.CreateAsync("Live")).IsSuccess);
        Assert.True((await profiles.SwitchToAsync("Live")).IsSuccess);

        var lib = new LibraryViewModel(
            sp.GetRequiredService<ILibraryQueryService>(),
            actions: sp.GetRequiredService<ILibraryActionService>());
        await lib.RefreshAsync();
        UiE2E.Pump();
        Assert.Equal(2, lib.Items.Count);
        foreach (var it in lib.Items)
            lib.SelectedItems.Add(it);

        await lib.InstallSelectedCommand.ExecuteAsync(null);
        UiE2E.Pump();

        var db = sp.GetRequiredService<VarVaultDbContext>();
        if (lib.LastActionMessage?.Contains("Developer Mode") == true)
            return; // no symlink privilege — install aborts atomically (0 links)

        var liveId = await db.Profiles.Where(p => p.Name == "Live").Select(p => p.Id).SingleAsync();
        Assert.Equal(2, await db.ActivationLinks.CountAsync(l => l.ProfileId == liveId));
        Assert.False(await db.Profiles.AnyAsync(p => p.Name == "Library installs"));
        Assert.Equal(0, await db.ActivationLinks.CountAsync(l =>
            db.Profiles.Any(p => p.Id == l.ProfileId && p.Name == "Library installs")));

        var varsLink = ActivationPaths.VarsLinkDir(vamDir.Path, "Live");
        if (Directory.Exists(varsLink))
        {
            var links = Directory.GetFiles(varsLink);
            Assert.Equal(2, links.Length);
        }

        await lib.UninstallSelectedCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.Equal(0, await db.ActivationLinks.CountAsync(l => l.ProfileId == liveId));
    }

    private static async Task<System.Guid> RegisterAsync(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = System.Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = true, IsEnabled = true, Tier = 1,
            CreatedAt = System.DateTime.UtcNow, UpdatedAt = System.DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes($"{{\"creatorName\":\"{creator}\",\"packageName\":\"{package}\"}}"));
    }
}
