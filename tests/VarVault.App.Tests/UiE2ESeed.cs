using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Seeds a real catalog (repository + packages + var files + the materialized read model) with real files
/// on disk, so UI-E2E tests can drive the actual library grid over real data and assert real file/DB effects.
/// Mirrors production rows exactly (the projection itself is proven by the E2E indexing suite). (19-Audit.)
/// </summary>
public static class UiE2ESeed
{
    public sealed record SeededPackage(long PackageId, IReadOnlyList<long> VarFileIds, IReadOnlyList<string> Paths);

    /// <summary>Register an online HDD repository rooted at <paramref name="mountPath"/>.</summary>
    public static async Task<Guid> SeedRepositoryAsync(TestHost host, string mountPath)
    {
        var id = Guid.NewGuid();
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Repositories.Add(new Repository
        {
            Id = id, Name = "ui-e2e", MountPath = mountPath, Tier = 1, MediaType = MediaType.Hdd,
            IsOnline = true, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Seed one package with <paramref name="copyCount"/> physical copies (same content hash → verified
    /// duplicates when &gt;1). Writes real files under the repo and a matching <see cref="PackageListItem"/>
    /// so the library grid shows the row.
    /// </summary>
    public static async Task<SeededPackage> SeedPackageAsync(
        TestHost host, Guid repoId, string mountPath, long packageId, string identity, int copyCount,
        int reverseDependents = 0, bool isActive = false)
    {
        var parts = identity.Split('.');
        var creator = parts[0];
        var pkgName = parts[1];
        var version = parts.Length > 2 ? parts[2] : "1";
        var hash = "hash-" + identity;

        var varFileIds = new List<long>();
        var paths = new List<string>();

        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();

        db.Packages.Add(new Package
        {
            Id = packageId, VarName = identity, IdentityKey = identity.ToUpperInvariant(),
            Creator = creator, PackageName = pkgName, VersionToken = version, VersionSort = 1,
            ReverseDependentCount = reverseDependents,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });

        for (var i = 0; i < copyCount; i++)
        {
            var rel = $"{identity}.copy{i}.var";
            var full = Path.Combine(mountPath, rel);
            await File.WriteAllTextAsync(full, "var-bytes-" + identity);
            var vfId = packageId * 100 + i;
            db.VarFiles.Add(new VarFile
            {
                Id = vfId, PackageId = packageId, RepositoryId = repoId, RelativePath = rel,
                SizeBytes = 100, ContentHash = hash, ContentSignature = "sig-" + identity,
                FileMtime = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            });
            varFileIds.Add(vfId);
            paths.Add(full);
        }

        db.PackageListItems.Add(new PackageListItem
        {
            PackageId = packageId, VarName = identity, Creator = creator, PackageName = pkgName,
            VersionToken = version, PrimaryType = ContentType.Unknown, TotalSize = 100,
            OnlineInstanceCount = copyCount, TotalInstanceCount = copyCount, IsSingleCopy = copyCount <= 1,
            Class = ContentClass.Cold, ActualTierMin = 1, IsActive = isActive, AddedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();

        // Seed the FTS5 trigram index (populated by indexing in production) so ≥3-char search finds the row.
        var blob = $"{identity} {creator} {pkgName}";
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO PackageSearch(rowid, Blob) VALUES ({0}, {1})", packageId, blob);

        return new SeededPackage(packageId, varFileIds, paths);
    }
}
