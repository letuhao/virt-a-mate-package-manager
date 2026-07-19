using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N10 · Bulk library actions: add-to-preset, delete (predicate-gated → trash), export-txt.
/// (16-checklist BE-N10.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class LibraryActionServiceFlowTests
{
    [Fact]
    public async Task Add_to_preset_export_and_predicate_gated_delete()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repo = new TempDirectory();
        var repoId = Guid.NewGuid();
        foreach (var n in new[] { "a1", "a2", "b1" })
            await File.WriteAllTextAsync(Path.Combine(repo.Path, $"{n}.var"), n);

        using (var scope = host.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = repo.Path, Tier = 1, MediaType = MediaType.Hdd,
                IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            Pkg(db, 1, "A.P.1");
            db.VarFiles.Add(Var(10, 1, repoId, "a1.var", "hashA"));
            db.VarFiles.Add(Var(11, 1, repoId, "a2.var", "hashA")); // verified duplicate of a1
            Pkg(db, 2, "B.P.1");
            db.VarFiles.Add(Var(20, 2, repoId, "b1.var", "hashB")); // single copy
            await db.SaveChangesAsync();
        }

        using var run = host.Host.Services.CreateScope();
        var actions = run.ServiceProvider.GetRequiredService<ILibraryActionService>();
        var presets = run.ServiceProvider.GetRequiredService<IPresetService>();

        var preset = await presets.CreateAsync("test", []);
        Assert.True(preset.IsSuccess);

        var added = await actions.AddToPresetAsync(preset.Value.Id, [1, 2]);
        Assert.Equal(2, added.Succeeded);

        var txt = await actions.ExportTxtAsync([1, 2]);
        Assert.Contains("A.P.1", txt);
        Assert.Contains("B.P.1", txt);

        // Delete a verified duplicate → trashed; delete the single copy → blocked.
        var delDup = await actions.DeleteAsync([11]);
        Assert.Equal(1, delDup.Succeeded);
        Assert.False(File.Exists(Path.Combine(repo.Path, "a2.var")));

        var delSingle = await actions.DeleteAsync([20]);
        Assert.Equal(1, delSingle.Failed);
        Assert.True(File.Exists(Path.Combine(repo.Path, "b1.var")));
    }

    private static void Pkg(VarVaultDbContext db, long id, string name) =>
        db.Packages.Add(new Package
        {
            Id = id, VarName = name, IdentityKey = name.ToUpperInvariant(), Creator = name.Split('.')[0],
            PackageName = name.Split('.')[1], VersionToken = "1", VersionSort = 1,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });

    private static VarFile Var(long id, long pkgId, Guid repoId, string rel, string hash) => new()
    {
        Id = id, PackageId = pkgId, RepositoryId = repoId, RelativePath = rel, SizeBytes = 100,
        ContentHash = hash, FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
    };
}
