using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Library;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using Microsoft.Extensions.DependencyInjection;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class ProfilePackageLinkTests
{
    [Fact]
    public async Task Sync_preserves_InstalledAt_on_rebuild()
    {
        using var fx = new SqliteTestDatabase();
        var installed = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var repoId = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Profiles.Add(new Profile { Id = 1, Name = "Default", DirPath = "x", CreatedAt = installed, UpdatedAt = installed, IsActive = true });
            db.Packages.Add(new Package { Id = 10, VarName = "A.B.1", IdentityKey = "A.B.1", Creator = "A", PackageName = "B", VersionToken = "1", VersionSort = 1, FirstSeenAt = installed, LastIndexedAt = installed });
            db.Repositories.Add(new Repository { Id = repoId, Name = "r", MountPath = @"C:\r", Tier = 1, MediaType = MediaType.Nvme, IsOnline = true, IsEnabled = true, CreatedAt = installed, UpdatedAt = installed });
            db.VarFiles.Add(new VarFile { Id = 100, PackageId = 10, RepositoryId = repoId, RelativePath = "a.var", SizeBytes = 1, FileMtime = installed, IndexedAt = installed });
            db.ActivationLinks.Add(new ActivationLink { Id = 1, ProfileId = 1, VarFileId = 100, LinkPath = @"C:\link", LinkKind = LinkKind.Install, LinkType = LinkType.Symlink, Reason = ActivationReason.Explicit });
            db.ProfilePackageLinks.Add(new ProfilePackageLink { ProfileId = 1, PackageId = 10, InstalledAt = installed, Reason = ActivationReason.Explicit, UpdatedAt = installed });
            db.PackageListItems.Add(new PackageListItem { PackageId = 10, VarName = "A.B.1", Creator = "A", PackageName = "B", VersionToken = "1", AddedAt = installed });
            await db.SaveChangesAsync();
        }

        using var scope = fx.NewContext();
        var svc = new EfProfilePackageLinkService(new FakeClock(installed.AddDays(1)), new InlineWriteQueue(scope));
        await svc.SyncFromActivationLinksAsync(1);
        await svc.RefreshActiveProfileReadModelAsync();

        var link = await scope.ProfilePackageLinks.SingleAsync();
        Assert.Equal(installed, link.InstalledAt);
        var item = await scope.PackageListItems.SingleAsync();
        Assert.True(item.IsActive);
        Assert.Equal(installed, item.InstalledAt);
    }

    [Fact]
    public async Task Added_and_installed_sort_orders_apply()
    {
        using var fx = new SqliteTestDatabase();
        var t0 = DateTime.UtcNow.AddDays(-10);
        var t1 = DateTime.UtcNow.AddDays(-5);
        using (var db = fx.NewContext())
        {
            db.Packages.AddRange(
                new Package { Id = 1, VarName = "Z.Z.1", IdentityKey = "Z.Z.1", Creator = "Z", PackageName = "Z", VersionToken = "1", VersionSort = 1, FirstSeenAt = t1, LastIndexedAt = t1 },
                new Package { Id = 2, VarName = "A.A.1", IdentityKey = "A.A.1", Creator = "A", PackageName = "A", VersionToken = "1", VersionSort = 1, FirstSeenAt = t0, LastIndexedAt = t0 });
            db.PackageListItems.AddRange(
                new PackageListItem { PackageId = 1, VarName = "Z.Z.1", Creator = "Z", PackageName = "Z", VersionToken = "1", AddedAt = t1, InstalledAt = null, Class = ContentClass.Cold },
                new PackageListItem { PackageId = 2, VarName = "A.A.1", Creator = "A", PackageName = "A", VersionToken = "1", AddedAt = t0, InstalledAt = t1, IsActive = true, Class = ContentClass.Cold });
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var svc = new EfLibraryQueryService(read);
        var added = await svc.GetOrderedIdsAsync(new LibraryQuery(Sort: LibrarySort.Added, Descending: true));
        Assert.Equal([1L, 2L], added);
        var installed = await svc.GetOrderedIdsAsync(new LibraryQuery(Sort: LibrarySort.Installed, Descending: true));
        Assert.Equal(2L, installed[0]);
        Assert.Equal(1L, installed[1]); // null installed last
    }

    [Fact]
    public async Task Default_query_is_Added_descending_then_VarName()
    {
        using var fx = new SqliteTestDatabase();
        var noon = new DateTime(2026, 7, 21, 5, 0, 0, DateTimeKind.Utc);   // same AddedAt
        var afternoon = new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc);
        using (var db = fx.NewContext())
        {
            db.Packages.AddRange(
                new Package { Id = 1, VarName = "Z.Late.1", IdentityKey = "Z.Late.1", Creator = "Z", PackageName = "Late", VersionToken = "1", VersionSort = 1, FirstSeenAt = afternoon, LastIndexedAt = afternoon },
                new Package { Id = 2, VarName = "M.Same.1", IdentityKey = "M.Same.1", Creator = "M", PackageName = "Same", VersionToken = "1", VersionSort = 1, FirstSeenAt = noon, LastIndexedAt = noon },
                new Package { Id = 3, VarName = "A.Same.1", IdentityKey = "A.Same.1", Creator = "A", PackageName = "Same", VersionToken = "1", VersionSort = 1, FirstSeenAt = noon, LastIndexedAt = noon });
            db.PackageListItems.AddRange(
                new PackageListItem { PackageId = 1, VarName = "Z.Late.1", Creator = "Z", PackageName = "Late", VersionToken = "1", AddedAt = afternoon, Class = ContentClass.Cold },
                new PackageListItem { PackageId = 2, VarName = "M.Same.1", Creator = "M", PackageName = "Same", VersionToken = "1", AddedAt = noon, Class = ContentClass.Cold },
                new PackageListItem { PackageId = 3, VarName = "A.Same.1", Creator = "A", PackageName = "Same", VersionToken = "1", AddedAt = noon, Class = ContentClass.Cold });
            await db.SaveChangesAsync();
        }

        using var read = fx.NewContext();
        var svc = new EfLibraryQueryService(read);
        // LibraryQuery() defaults must be Added ↓ then VarName ↑ (A before M among equal timestamps).
        var ids = await svc.GetOrderedIdsAsync(new LibraryQuery());
        Assert.Equal([1L, 3L, 2L], ids);
    }

    [Fact]
    public async Task Refresh_active_read_model_at_scale_clears_stale_and_sets_new()
    {
        using var fx = new SqliteTestDatabase();
        var installed = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var repoId = Guid.NewGuid();
        using (var db = fx.NewContext())
        {
            db.Profiles.Add(new Profile
            {
                Id = 1, Name = "Default", DirPath = "x", CreatedAt = installed, UpdatedAt = installed, IsActive = true,
            });
            db.Repositories.Add(new Repository
            {
                Id = repoId, Name = "r", MountPath = @"C:\r", Tier = 1, MediaType = MediaType.Nvme,
                IsOnline = true, IsEnabled = true, CreatedAt = installed, UpdatedAt = installed,
            });

            for (var i = 1; i <= 1000; i++)
            {
                db.Packages.Add(new Package
                {
                    Id = i, VarName = $"P.Pkg{i}.1", IdentityKey = $"P.PKG{i}.1", Creator = "P", PackageName = $"Pkg{i}",
                    VersionToken = "1", VersionSort = 1, FirstSeenAt = installed, LastIndexedAt = installed,
                });
                db.PackageListItems.Add(new PackageListItem
                {
                    PackageId = i, VarName = $"P.Pkg{i}.1", Creator = "P", PackageName = $"Pkg{i}", VersionToken = "1",
                    AddedAt = installed, IsActive = i <= 50, InstalledAt = i <= 50 ? installed : null,
                    Class = ContentClass.Cold,
                });
            }

            for (var i = 1; i <= 10; i++)
            {
                var vfId = 1000 + i;
                db.VarFiles.Add(new VarFile
                {
                    Id = vfId, PackageId = i, RepositoryId = repoId, RelativePath = $"p{i}.var",
                    SizeBytes = 1, FileMtime = installed, IndexedAt = installed,
                });
                db.ActivationLinks.Add(new ActivationLink
                {
                    Id = vfId, ProfileId = 1, VarFileId = vfId, LinkPath = $@"C:\link{i}",
                    LinkKind = LinkKind.Install, LinkType = LinkType.Symlink, Reason = ActivationReason.Explicit,
                });
            }

            await db.SaveChangesAsync();
        }

        using var scope = fx.NewContext();
        var svc = new EfProfilePackageLinkService(new FakeClock(installed.AddDays(1)), new InlineWriteQueue(scope));
        await svc.SyncFromActivationLinksAsync(1);
        await svc.RefreshActiveProfileReadModelAsync();

        Assert.Equal(10, await scope.PackageListItems.CountAsync(x => x.IsActive));
        Assert.Equal(990, await scope.PackageListItems.CountAsync(x => !x.IsActive));
        Assert.All(await scope.PackageListItems.Where(x => x.IsActive).ToListAsync(), x => Assert.NotNull(x.InstalledAt));
    }

    private sealed class InlineWriteQueue(VarVaultDbContext db) : VarVault.Sdk.Threading.IWriteQueue
    {
        private static IServiceProvider Provider(VarVaultDbContext context) =>
            new ServiceCollection().AddSingleton(context).BuildServiceProvider();

        public Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> action, VarVault.Sdk.Threading.WritePriority priority = VarVault.Sdk.Threading.WritePriority.Normal, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
        public Task EnqueueAsync(Func<CancellationToken, Task> action, VarVault.Sdk.Threading.WritePriority priority = VarVault.Sdk.Threading.WritePriority.Normal, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
        public Task<T> EnqueueScopedAsync<T>(Func<IServiceProvider, CancellationToken, Task<T>> action, VarVault.Sdk.Threading.WritePriority priority = VarVault.Sdk.Threading.WritePriority.Normal, CancellationToken cancellationToken = default) =>
            action(Provider(db), cancellationToken);
        public Task EnqueueScopedAsync(Func<IServiceProvider, CancellationToken, Task> action, VarVault.Sdk.Threading.WritePriority priority = VarVault.Sdk.Threading.WritePriority.Normal, CancellationToken cancellationToken = default) =>
            action(Provider(db), cancellationToken);
    }
}
