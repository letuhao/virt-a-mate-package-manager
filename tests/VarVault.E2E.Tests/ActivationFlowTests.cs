using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Activation;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Activation link building: a preset's members + dependency closure become real per-var file symlinks on
/// disk under the profile's <c>___VarsLink___</c> (install) / <c>___MissingVarLink___</c> (alias) folders,
/// with ActivationLink rows mirroring the filesystem. Skips file-level assertions where symlink privilege
/// is unavailable (no Developer Mode). (Spec 21; checklist 22 · P4/P5/T7.1.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ActivationFlowTests
{
    [Fact]
    public async Task Builds_links_for_members_and_their_closure()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1"); // member, depends on Base
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");             // pulled in as a dependency
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);
        if (result.PrivilegeFailures > 0) return; // no Developer Mode — cannot materialize links here

        Assert.Equal(2, result.LinksCreated); // Look + Base
        Assert.Equal(0, result.MissingPackages);

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var look = await db.Packages.FirstAsync(p => p.VarName == "A.Look.1");
        var baseP = await db.Packages.FirstAsync(p => p.VarName == "A.Base.1");

        var links = await db.ActivationLinks.ToListAsync();
        Assert.Equal(2, links.Count);
        // Each link is keyed by a VarFile, not a package, and carries a reason. (3.4/3.5)
        var lookVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == look.Id)).Id;
        var baseVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == baseP.Id)).Id;
        Assert.Contains(links, l => l.VarFileId == lookVarId && l.Reason == ActivationReason.Explicit);
        Assert.Contains(links, l => l.VarFileId == baseVarId && l.Reason == ActivationReason.DependencyOf);
        Assert.All(links, l => Assert.Equal(LinkKind.Install, l.LinkKind));

        // The links are REAL file symlinks named by identity, resolving to the source var. (T4.1)
        var varsLink = ActivationPaths.VarsLinkDir(vamDir.Path, "P");
        var lookLink = Path.Combine(varsLink, "A.Look.1.var");
        var baseLink = Path.Combine(varsLink, "A.Base.1.var");
        Assert.True(File.Exists(lookLink));
        Assert.True(File.Exists(baseLink));
        Assert.Equal(Path.Combine(repoDir.Path, "A.Look.1.var"), new FileInfo(lookLink).LinkTarget);
        Assert.Equal(Path.Combine(repoDir.Path, "A.Base.1.var"), new FileInfo(baseLink).LinkTarget);
    }

    [Fact]
    public async Task Builds_links_for_four_level_dependency_chain()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Mid.1");
        WriteVar(repoDir, "A.Mid.1.var", "A", "Mid", "A.Deep.1");
        WriteVar(repoDir, "A.Deep.1.var", "A", "Deep", "A.Leaf.1");
        WriteVar(repoDir, "A.Leaf.1.var", "A", "Leaf");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);
        if (result.PrivilegeFailures > 0) return;

        Assert.Equal(4, result.LinksCreated);
        Assert.Equal(0, result.MissingPackages);
        Assert.Equal(0, result.UnresolvedDependencies);

        var varsLink = ActivationPaths.VarsLinkDir(vamDir.Path, "P");
        Assert.True(File.Exists(Path.Combine(varsLink, "A.Look.1.var")));
        Assert.True(File.Exists(Path.Combine(varsLink, "A.Mid.1.var")));
        Assert.True(File.Exists(Path.Combine(varsLink, "A.Deep.1.var")));
        Assert.True(File.Exists(Path.Combine(varsLink, "A.Leaf.1.var")));
    }

    [Fact]
    public async Task Reports_offline_copy_and_unresolved_transitive_separately()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base", "Ghost.Missing.1");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();

        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var repo = await db.Repositories.FirstAsync(r => r.Id == repoId);
        repo.IsOnline = false;
        await db.SaveChangesAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Look.1"])).Value;
        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);
        if (result.PrivilegeFailures > 0) return;

        Assert.Equal(0, result.LinksCreated);
        Assert.Equal(2, result.MissingPackages); // Look + Base offline
        Assert.True(result.UnresolvedDependencies >= 1); // Ghost.Missing.1
    }

    [Fact]
    public async Task Rebuild_is_idempotent_on_disk_and_in_db()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["A.Solo.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        var first = await activation.BuildProfileLinksAsync(preset.Id);
        if (first.PrivilegeFailures > 0) return;
        var second = await activation.BuildProfileLinksAsync(preset.Id); // rebuild

        Assert.Equal(1, second.LinksCreated);   // still present
        Assert.Equal(0, second.LinksRemoved);   // nothing churned
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Equal(1, await db.ActivationLinks.CountAsync()); // not duplicated
        Assert.True(File.Exists(Path.Combine(ActivationPaths.VarsLinkDir(vamDir.Path, "P"), "A.Solo.1.var")));
    }

    [Fact]
    public async Task Deactivation_reference_counts_shared_dependencies()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);

        // Two looks that both depend on the same Shared package.
        WriteVar(repoDir, "A.LookOne.1.var", "A", "LookOne", "A.Shared.1");
        WriteVar(repoDir, "A.LookTwo.1.var", "A", "LookTwo", "A.Shared.1");
        WriteVar(repoDir, "A.Shared.1.var", "A", "Shared");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var one = await db.Packages.FirstAsync(p => p.VarName == "A.LookOne.1");
        var shared = await db.Packages.FirstAsync(p => p.VarName == "A.Shared.1");

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.LookOne.1", "A.LookTwo.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        var built = await activation.BuildProfileLinksAsync(preset.Id);
        if (built.PrivilegeFailures > 0) return;
        Assert.Equal(3, await db.ActivationLinks.CountAsync()); // LookOne + LookTwo + Shared

        // Deactivate LookOne → Shared stays (LookTwo still needs it); LookOne's own link goes.
        var afterOne = await activation.DeactivateAsync(preset.Id, one.Id);
        Assert.Equal(2, afterOne.LinksCreated); // LookTwo + Shared remain present
        Assert.Equal(1, afterOne.LinksRemoved); // LookOne removed
        var sharedVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == shared.Id)).Id;
        Assert.True(await db.ActivationLinks.AnyAsync(l => l.VarFileId == sharedVarId)); // ref-counted, not dropped

        var varsLink = ActivationPaths.VarsLinkDir(vamDir.Path, "P");
        Assert.False(File.Exists(Path.Combine(varsLink, "A.LookOne.1.var"))); // link file deleted
        Assert.True(File.Exists(Path.Combine(varsLink, "A.Shared.1.var")));    // shared link survives
    }

    [Fact]
    public async Task Rebuild_preserves_user_made_links()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["A.Solo.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        var built = await activation.BuildProfileLinksAsync(preset.Id);
        if (built.PrivilegeFailures > 0) return;
        var profile = await db.Profiles.FirstAsync();
        var solo = await db.VarFiles.FirstAsync();

        // A user-made link (no preset attribution) in the same profile.
        db.ActivationLinks.Add(new ActivationLink
        {
            ProfileId = profile.Id, VarFileId = solo.Id, LinkPath = "user/link.var",
            LinkKind = LinkKind.Install, LinkType = LinkType.Symlink, Reason = ActivationReason.Explicit,
            RequestedByPresetId = null, // user-made
        });
        await db.SaveChangesAsync();

        await activation.BuildProfileLinksAsync(preset.Id); // rebuild

        // The user-made link survives; only the app's own links were replaced. (3.12)
        Assert.True(await db.ActivationLinks.AnyAsync(l => l.RequestedByPresetId == null));
    }

    [Fact]
    public async Task Rescue_removes_app_links_and_temp_cleanup_removes_temp_links()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["A.Solo.1"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        var built = await activation.BuildProfileLinksAsync(preset.Id);
        if (built.PrivilegeFailures > 0) return;
        var profile = await db.Profiles.FirstAsync();
        var solo = await db.VarFiles.FirstAsync();
        var soloLink = Path.Combine(ActivationPaths.VarsLinkDir(vamDir.Path, "P"), "A.Solo.1.var");
        Assert.True(File.Exists(soloLink));

        // Add a temp link.
        db.ActivationLinks.Add(new ActivationLink
        {
            ProfileId = profile.Id, VarFileId = solo.Id, LinkPath = "temp/link.var",
            LinkKind = LinkKind.Temp, LinkType = LinkType.Symlink, Reason = ActivationReason.Temp,
            RequestedByPresetId = preset.Id,
        });
        await db.SaveChangesAsync();

        Assert.Equal(1, await activation.CleanTempLinksAsync(profile.Id)); // 3.14 — temp cleaned
        Assert.False(await db.ActivationLinks.AnyAsync(l => l.LinkKind == LinkKind.Temp));

        var removed = await activation.RescueAsync(profile.Id); // 3.13 — deactivate all
        Assert.True(removed >= 1);
        Assert.Empty(await db.ActivationLinks.ToListAsync());
        Assert.False(File.Exists(soloLink)); // real link file removed from disk
    }

    [Fact]
    public async Task Path_unavailable_is_reported_instead_of_silent_zero_success()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        // Intentionally leave VaM path unset.
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Solo.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>()
            .BuildProfileLinksAsync(preset.Id);

        Assert.Equal(1, result.PathUnavailable);
        Assert.Equal(0, result.LinksCreated);
        Assert.Equal(0, result.MissingPackages);
    }

    [Fact]
    public async Task Unresolved_preset_member_counts_as_unresolved_dependency()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Solo.1.var", "A", "Solo");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["A.Solo.1", "Ghost.Gone.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>()
            .BuildProfileLinksAsync(preset.Id);
        if (result.PrivilegeFailures > 0) return;

        Assert.True(result.UnresolvedDependencies >= 1);
        Assert.Equal(0, result.PathUnavailable);
    }

    [Fact]
    public async Task Offline_alias_target_is_not_double_counted_in_missing()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Real.Target.1.var", "Real", "Target");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var target = await db.Packages.FirstAsync(p => p.VarName == "Real.Target.1");
        var repo = await db.Repositories.FirstAsync(r => r.Id == repoId);
        repo.IsOnline = false;
        await db.SaveChangesAsync();

        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>()
            .CreateAsync("P", ["Renamed.Old.1"])).Value;
        db.VarAliases.Add(new VarAlias
        {
            MissingRefKey = VarVault.Domain.Identity.IdentityFold.Compute("Renamed.Old.1"),
            MissingRefRaw = "Renamed.Old.1",
            ResolvedPackageId = target.Id,
            Scope = AliasScope.Global,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>()
            .BuildProfileLinksAsync(preset.Id);
        if (result.PrivilegeFailures > 0) return;

        Assert.Equal(0, result.LinksCreated);
        Assert.Equal(1, result.MissingPackages); // target once — not member + alias double-count
        Assert.Equal(0, result.UnresolvedDependencies);
    }

    [Fact]
    public async Task Persistent_alias_reapplies_on_every_build()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Real.Target.1.var", "Real", "Target");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await SeedVamRoot(scope, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var target = await db.Packages.FirstAsync(p => p.VarName == "Real.Target.1");

        // Preset member references a renamed (missing) package; a global alias maps it to the target.
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["Renamed.Old.1"])).Value;
        db.VarAliases.Add(new VarAlias
        {
            MissingRefKey = VarVault.Domain.Identity.IdentityFold.Compute("Renamed.Old.1"),
            MissingRefRaw = "Renamed.Old.1",
            ResolvedPackageId = target.Id,
            Scope = AliasScope.Global,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();
        var first = await activation.BuildProfileLinksAsync(preset.Id);
        if (first.PrivilegeFailures > 0) return;
        Assert.Equal(1, first.LinksCreated); // the alias link stands in for the missing member

        var second = await activation.BuildProfileLinksAsync(preset.Id); // rebuild — alias still applied (3.9)
        Assert.Equal(1, second.LinksCreated);
        var targetVarId = (await db.VarFiles.FirstAsync(v => v.PackageId == target.Id)).Id;
        Assert.True(await db.ActivationLinks.AnyAsync(l => l.VarFileId == targetVarId && l.LinkKind == LinkKind.Alias));

        // The alias link is named after the MISSING ref and lives under ___MissingVarLink___, resolving to the target.
        var aliasLink = Path.Combine(ActivationPaths.MissingVarLinkDir(vamDir.Path, "P"), "Renamed.Old.1.var");
        Assert.True(File.Exists(aliasLink));
        Assert.Equal(Path.Combine(repoDir.Path, "Real.Target.1.var"), new FileInfo(aliasLink).LinkTarget);
    }

    private static async Task SeedVamRoot(IServiceScope scope, string vamRoot) =>
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamRoot);

    private static async Task<Guid> Register(TestHost host, string path)
    {
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        var id = Guid.NewGuid();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "t", MountPath = path, IsOnline = true, IsEnabled = true, Tier = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static void WriteVar(TempDirectory dir, string fileName, string creator, string package, params string[] deps)
    {
        var path = Path.Combine(dir.Path, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var depObj = string.Join(",", deps.Select(d => "\"" + d + "\":{}"));
        Add(zip, "meta.json", "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}");
        Add(zip, "Custom/Hair/h.vam", "x");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
