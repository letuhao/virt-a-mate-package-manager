using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Safety;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// BE-N6 · Trash &amp; backup: list/restore/purge trashed items and create/list catalog backups.
/// (16-checklist BE-N6.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class TrashQueryServiceFlowTests
{
    [Fact]
    public async Task List_restore_purge_and_backup_round_trip()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var dir = new TempDirectory();
        var filePath = Path.Combine(dir.Path, "victim.var");
        await File.WriteAllTextAsync(filePath, "bytes");

        var trash = host.Host.Services.GetRequiredService<ITrashService>();
        var trashed = await trash.TrashAsync(filePath, "test", default);
        Assert.True(trashed.IsSuccess);
        var id = trashed.Value.Id;
        Assert.False(File.Exists(filePath)); // moved to trash

        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ITrashQueryService>();

            var list = await svc.ListAsync();
            Assert.Contains(list, t => t.Id == id && t.Reason == "test");

            // Restore → original back, trash empty.
            Assert.True((await svc.RestoreAsync(id)).IsSuccess);
            Assert.True(File.Exists(filePath));
            Assert.DoesNotContain(await svc.ListAsync(), t => t.Id == id);
        }

        // Trash again, then purge permanently.
        var trashed2 = await trash.TrashAsync(filePath, "again", default);
        var id2 = trashed2.Value.Id;
        using (var scope = host.Host.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ITrashQueryService>();
            Assert.True((await svc.PurgeAsync(id2)).IsSuccess);
            Assert.DoesNotContain(await svc.ListAsync(), t => t.Id == id2);

            // Backup now + list.
            var backup = await svc.BackupNowAsync();
            Assert.True(backup.IsSuccess, backup.Error.ToString());
            Assert.True(File.Exists(backup.Value.Path));
            Assert.Contains(await svc.ListBackupsAsync(), b => b.Path == backup.Value.Path);
        }
    }
}
