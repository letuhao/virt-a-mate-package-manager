using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-5 — the Library ops-bar "Install"/"Uninstall" (the mockup's two primary actions, absent per
/// audit 25 §5.2). Install activates the selection via a dedicated "Library installs" preset; Uninstall
/// removes them. Link materialization needs symlink privilege (Developer Mode); the test skips the on-disk
/// assertion when it's unavailable, but still exercises the whole VM→SDK path.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class LibraryActivateTests
{
    [AvaloniaFact]
    public async Task Install_selected_activates_then_uninstall_removes_links()
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

        var lib = new LibraryViewModel(
            sp.GetRequiredService<ILibraryQueryService>(),
            presets: sp.GetService<IPresetService>(),
            activation: sp.GetService<IActivationService>());
        await lib.RefreshAsync();
        UiE2E.Pump();
        Assert.Equal(2, lib.Items.Count);
        foreach (var it in lib.Items)
            lib.SelectedItems.Add(it);

        await lib.InstallSelectedCommand.ExecuteAsync(null);
        UiE2E.Pump();

        var db = sp.GetRequiredService<VarVaultDbContext>();
        if (lib.LastActionMessage?.Contains("Developer Mode") == true)
            return; // no symlink privilege in this environment — install aborts atomically (0 links)

        Assert.Equal(2, await db.ActivationLinks.CountAsync()); // both selected packages linked

        await lib.UninstallSelectedCommand.ExecuteAsync(null);
        UiE2E.Pump();
        Assert.Equal(0, await db.ActivationLinks.CountAsync()); // links removed
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
