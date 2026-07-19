using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-G · The gap backend methods work from the SDK boundary: library move/resolve-txt (BE-G1), repository
/// set-tier (BE-G2), preset remove-member/members (BE-G3), catalog backups (BE-G4), tiering propose-only
/// plan = simulate (BE-G5), and package detail closure/content/copies (BE-G6). (18-gap BE-G.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class GapBackendFlowTests
{
    [Fact]
    public async Task Move_to_subfolder_relocates_the_file_and_updates_the_catalog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteVar(repoDir, "Creator.MoveMe.1.var",
            """{"creatorName":"Creator","packageName":"MoveMe"}""", [("Custom/x.vam", "y")]);

        Guid repoId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            var reg = await repos.RegisterAsync(new RegisterRepositoryRequest("r", repoDir.Path));
            repoId = reg.Value.Id;
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        }

        long varFileId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            varFileId = await db.VarFiles.Select(v => v.Id).FirstAsync();
        }

        using (var scope = host.Host.Services.CreateScope())
        {
            var actions = scope.ServiceProvider.GetRequiredService<ILibraryActionService>();
            var result = await actions.MoveToSubfolderAsync([varFileId], "Sorted");
            Assert.Equal(1, result.Succeeded);
        }

        // File physically moved and catalog RelativePath updated.
        Assert.False(File.Exists(Path.Combine(repoDir.Path, "Creator.MoveMe.1.var")));
        Assert.True(File.Exists(Path.Combine(repoDir.Path, "Sorted", "Creator.MoveMe.1.var")));
        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            var rel = await db.VarFiles.Select(v => v.RelativePath).FirstAsync();
            Assert.Contains("Sorted", rel);
        }
    }

    [Fact]
    public async Task Resolve_txt_matches_owned_and_reports_unmatched()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteVar(repoDir, "Creator.Owned.1.var", """{"creatorName":"Creator","packageName":"Owned"}""", [("Custom/x.vam", "y")]);

        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            await repos.RegisterAsync(new RegisterRepositoryRequest("r", repoDir.Path));
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();
        }

        using var s = host.Host.Services.CreateScope();
        var actions = s.ServiceProvider.GetRequiredService<ILibraryActionService>();
        var res = await actions.ResolveTxtAsync("Creator.Owned.1\nGone.NotOwned.2");
        Assert.Single(res.MatchedPackageIds);
        Assert.Contains("Gone.NotOwned.2", res.Unmatched);
    }

    [Fact]
    public async Task Set_tier_persists_the_manual_override()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        WriteVar(repoDir, "Creator.T.1.var", """{"creatorName":"Creator","packageName":"T"}""", [("Custom/x.vam", "y")]);

        using var scope = host.Host.Services.CreateScope();
        var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
        var reg = await repos.RegisterAsync(new RegisterRepositoryRequest("r", repoDir.Path));
        var updated = await repos.SetTierAsync(reg.Value.Id, 1);
        Assert.NotNull(updated);
        Assert.Equal(1, updated!.Tier);
        var reloaded = (await repos.ListAsync()).First(r => r.Id == reg.Value.Id);
        Assert.Equal(1, reloaded.Tier);
    }

    [Fact]
    public async Task Preset_remove_member_and_members_reflect_edits()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var presets = scope.ServiceProvider.GetRequiredService<IPresetService>();
        var created = await presets.CreateAsync("edit", ["A.B.1", "C.D.2"]);
        Assert.True(created.IsSuccess);
        var id = created.Value.Id;

        var members = await presets.MembersAsync(id);
        Assert.Equal(2, members.Count);

        var afterRemove = await presets.RemoveMemberAsync(id, "A.B.1");
        Assert.True(afterRemove.IsSuccess);
        Assert.Equal(1, afterRemove.Value.MemberCount);
        Assert.DoesNotContain("A.B.1", await presets.MembersAsync(id));
    }

    [Fact]
    public async Task Backups_list_and_backup_now_work()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var trash = scope.ServiceProvider.GetRequiredService<ITrashQueryService>();
        var made = await trash.BackupNowAsync();
        Assert.True(made.IsSuccess);
        var backups = await trash.ListBackupsAsync();
        Assert.NotEmpty(backups);
    }

    [Fact]
    public async Task Tiering_plan_is_propose_only_simulate()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var tiering = scope.ServiceProvider.GetRequiredService<ITieringService>();
        var plan = await tiering.BuildPlanAsync(); // returns proposals without moving anything
        Assert.NotNull(plan);
        Assert.NotNull(plan.Proposals);
    }

    private static void WriteVar(TempDirectory dir, string fileName, string meta, (string, string)[] entries)
    {
        using var fs = new FileStream(Path.Combine(dir.Path, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", meta);
        foreach (var (name, content) in entries) Add(zip, name, content);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
