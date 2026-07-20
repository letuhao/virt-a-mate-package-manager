using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
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

/// <summary>Activation edge cases: privilege abort, profile reconciliation, parallel safety. (Checklist 22 · T4.4/T3.3/T7.3.)</summary>
[Trait("Category", TestCategories.E2E)]
public sealed class ActivationEdgeCasesTests
{
    // A symlink service that denies file-link creation as if Developer Mode were off.
    private sealed class PrivilegeDeniedSymlinks : ISymlinkService
    {
        public Result CreateFile(string linkPath, string targetPath) => Result.Failure("symlink.privilege", "denied");
        public Result CreateDirectory(string linkPath, string targetPath) => Result.Failure("symlink.privilege", "denied");
        public Result DeleteLink(string linkPath) => Result.Success();
        public Result RepointDirectory(string linkPath, string newTargetPath) => Result.Failure("symlink.privilege", "denied");
        public string? ResolveTarget(string linkPath) => null;
        public bool IsLink(string path) => false;
    }

    // Stateful recorder: verifies the exact CreateFile/DeleteLink calls WITHOUT needing OS symlink privilege,
    // so the materialization logic is checked in every environment (incl. CI without Developer Mode).
    private sealed class RecordingSymlinks : ISymlinkService
    {
        public Dictionary<string, string> Links { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int CreateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public void ResetCounters() { CreateCalls = 0; DeleteCalls = 0; }
        public Result CreateFile(string linkPath, string targetPath) { CreateCalls++; Links[linkPath] = targetPath; return Result.Success(); }
        public Result CreateDirectory(string linkPath, string targetPath) { Links[linkPath] = targetPath; return Result.Success(); }
        public Result DeleteLink(string linkPath) { DeleteCalls++; Links.Remove(linkPath); return Result.Success(); }
        public Result RepointDirectory(string linkPath, string newTargetPath) { Links[linkPath] = newTargetPath; return Result.Success(); }
        public string? ResolveTarget(string linkPath) => Links.TryGetValue(linkPath, out var t) ? t : null;
        public bool IsLink(string path) => Links.ContainsKey(path);
    }

    [Fact]
    public async Task Activation_calls_CreateFile_with_identity_filenames_and_real_targets()
    {
        var recorder = new RecordingSymlinks();
        await using var host = TestHost.Create(
            withPersistence: true,
            configure: s => s.AddSingleton<ISymlinkService>(recorder));
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "Creator.Look.7.var", "Creator", "Look", "Creator.Base.1"); // verbatim version .7
        WriteVar(repoDir, "Creator.Base.1.var", "Creator", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["Creator.Look.7"])).Value;
        var activation = scope.ServiceProvider.GetRequiredService<IActivationService>();

        var result = await activation.BuildProfileLinksAsync(preset.Id);

        // Real CreateFile calls (not DB-rows-only); correct identity filenames; real targets.
        Assert.Equal(0, result.PrivilegeFailures);
        Assert.Equal(2, result.LinksCreated);
        Assert.Equal(2, recorder.CreateCalls);
        var varsLink = ActivationPaths.VarsLinkDir(vamDir.Path, "P");
        Assert.Contains(recorder.Links, kv => Path.GetFileName(kv.Key) == "Creator.Look.7.var");   // NOT numeric id
        Assert.Contains(recorder.Links, kv => Path.GetFileName(kv.Key) == "Creator.Base.1.var");
        Assert.All(recorder.Links.Keys, k => Assert.StartsWith(varsLink, k, StringComparison.OrdinalIgnoreCase));
        Assert.All(recorder.Links.Values, t => Assert.True(File.Exists(t), $"target should be a real file: {t}"));
        Assert.Equal(Path.Combine(repoDir.Path, "Creator.Look.7.var"),
            recorder.Links[Path.Combine(varsLink, "Creator.Look.7.var")]);

        // Idempotent rebuild — no churn.
        recorder.ResetCounters();
        var again = await activation.BuildProfileLinksAsync(preset.Id);
        Assert.Equal(2, again.LinksCreated);
        Assert.Equal(0, again.LinksRemoved);
        Assert.Equal(0, recorder.CreateCalls);
        Assert.Equal(0, recorder.DeleteCalls);
    }

    [Fact]
    public async Task Privilege_failure_aborts_without_partial_links()
    {
        await using var host = TestHost.Create(
            withPersistence: true,
            configure: s => s.AddSingleton<ISymlinkService>(new PrivilegeDeniedSymlinks()));
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.Look.1.var", "A", "Look", "A.Base.1");
        WriteVar(repoDir, "A.Base.1.var", "A", "Base");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        await scope.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync("P", ["A.Look.1"])).Value;

        var result = await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);

        Assert.True(result.PrivilegeFailures > 0);
        Assert.Equal(0, result.LinksCreated);
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        Assert.Equal(0, await db.ActivationLinks.CountAsync()); // atomic — no partial rows persisted
    }

    [Fact]
    public async Task Reconcile_derives_active_and_prunes_vanished_profile()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var vamDir = new TempDirectory();

        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        // One profile whose on-disk dir exists, one whose dir is missing.
        Directory.CreateDirectory(ActivationPaths.ProfileDir(vamDir.Path, "alive"));
        db.Profiles.Add(new Profile { Name = "alive", DirPath = $"{ActivationPaths.SwitchDirName}/alive", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.Profiles.Add(new Profile { Name = "ghost", DirPath = $"{ActivationPaths.SwitchDirName}/ghost", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var pruned = await scope.ServiceProvider.GetRequiredService<IActivationService>().ReconcileProfilesAsync();

        Assert.Equal(1, pruned); // ghost pruned
        Assert.True(await db.Profiles.AnyAsync(p => p.Name == "alive"));
        Assert.False(await db.Profiles.AnyAsync(p => p.Name == "ghost"));
    }

    [Fact]
    public async Task Parallel_activations_on_distinct_presets_are_consistent()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        using var vamDir = new TempDirectory();
        var repoId = await Register(host, repoDir.Path);
        WriteVar(repoDir, "A.One.1.var", "A", "One");
        WriteVar(repoDir, "A.Two.1.var", "A", "Two");
        await host.Get<IIndexingService>().IndexRepositoryAsync(repoId, repoDir.Path);

        using (var seed = host.Host.Services.CreateScope())
        {
            await seed.ServiceProvider.GetRequiredService<ISettingsService>().SetAsync(SettingKeys.VamPath, vamDir.Path);
            await seed.ServiceProvider.GetRequiredService<IDependencyResolver>().ResolveAllAsync();
        }

        // Each concurrent activation gets its own scope (its own DbContext), like the real app.
        async Task<ActivationBuildResult> ActivateAsync(string presetName, string member)
        {
            using var scope = host.Host.Services.CreateScope();
            var preset = (await scope.ServiceProvider.GetRequiredService<IPresetService>().CreateAsync(presetName, [member])).Value;
            return await scope.ServiceProvider.GetRequiredService<IActivationService>().BuildProfileLinksAsync(preset.Id);
        }

        var results = await Task.WhenAll(ActivateAsync("PA", "A.One.1"), ActivateAsync("PB", "A.Two.1"));
        if (results.Any(r => r.PrivilegeFailures > 0)) return;

        Assert.All(results, r => Assert.Equal(1, r.LinksCreated));
        Assert.True(File.Exists(Path.Combine(ActivationPaths.VarsLinkDir(vamDir.Path, "PA"), "A.One.1.var")));
        Assert.True(File.Exists(Path.Combine(ActivationPaths.VarsLinkDir(vamDir.Path, "PB"), "A.Two.1.var")));
    }

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
        var entry = zip.CreateEntry("meta.json", CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes("{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{" + depObj + "}}"));
    }
}
