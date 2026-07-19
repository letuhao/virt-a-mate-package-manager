using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF Core implementation of <see cref="ICatalogStore"/>: maps a precomputed <see cref="VarUpsert"/>
/// to Package/VarFile/ContentItem/Dependency/PackageContentCount rows and maintains the materialized
/// <see cref="PackageListItem"/> read model. Called only inside the single-writer queue. (Data-arch §5.4.)
/// </summary>
public sealed class EfCatalogStore(VarVaultDbContext db, IClock clock) : ICatalogStore
{
    public async Task<bool> RepositoryIsOnlineAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var repo = await db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken)
            .ConfigureAwait(false);
        return repo is { IsOnline: true };
    }

    public async Task<IReadOnlyList<ExistingVarFile>> ListVarFilesAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        return await db.VarFiles
            .AsNoTracking()
            .Where(v => v.RepositoryId == repositoryId)
            .Select(v => new ExistingVarFile(v.Id, v.RelativePath, v.SizeBytes, v.FileMtime, v.PackageId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<long?> ApplyAsync(VarUpsert upsert, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(upsert);
        var now = clock.UtcNow.UtcDateTime;

        Package? package = null;
        if (upsert.Identity is { } identity)
        {
            package = await db.Packages
                .FirstOrDefaultAsync(p => p.IdentityKey == identity.IdentityKey, cancellationToken)
                .ConfigureAwait(false);

            if (package is null)
            {
                package = new Package
                {
                    VarName = identity.VarName,
                    IdentityKey = identity.IdentityKey,
                    Creator = identity.Creator,
                    PackageName = identity.Package,
                    VersionToken = identity.VersionToken,
                    VersionSort = identity.VersionSort,
                    FirstSeenAt = now,
                };
                db.Packages.Add(package);
            }

            package.LastIndexedAt = now;
            package.MetaCreator = upsert.MetaCreator;
            package.MetaPackage = upsert.MetaPackage;
            package.MetaDivergent = upsert.MetaDivergent;
            package.LicenseType = upsert.LicenseType;
            package.Description = upsert.Description;
            package.ProgramVersion = upsert.ProgramVersion;

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // ensure package.Id
        }

        var varFile = await db.VarFiles
            .FirstOrDefaultAsync(v => v.RepositoryId == upsert.RepositoryId && v.RelativePath == upsert.RelativePath, cancellationToken)
            .ConfigureAwait(false);

        if (varFile is null)
        {
            varFile = new VarFile
            {
                RepositoryId = upsert.RepositoryId,
                RelativePath = upsert.RelativePath,
            };
            db.VarFiles.Add(varFile);
        }

        varFile.PackageId = package?.Id;
        varFile.SizeBytes = upsert.SizeBytes;
        varFile.FileMtime = upsert.FileMtimeUtc;
        varFile.ContentSignature = upsert.ContentSignature;
        varFile.PayloadSignature = upsert.PayloadSignature;
        varFile.ContentSignatureNoPath = upsert.ContentSignatureNoPath;
        varFile.EncodingHealth = upsert.EncodingHealth;
        varFile.DetectedCodepage = upsert.DetectedCodepage;
        varFile.BrokenEntryCount = upsert.BrokenEntryCount;
        varFile.IntegrityStatus = upsert.Integrity;
        varFile.QuarantineKind = upsert.Quarantine;
        varFile.IndexedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // ensure varFile.Id

        await ReplaceContentItemsAsync(varFile.Id, upsert.ContentItems, cancellationToken).ConfigureAwait(false);
        await ReplaceDependenciesAsync(varFile.Id, upsert.DependencyRefsRaw, upsert.EmbeddedRefsRaw, cancellationToken).ConfigureAwait(false);

        if (package is not null)
        {
            // Elect a canonical copy if the package has none yet; content counts follow the canonical.
            var isCanonical = package.CanonicalVarFileId is null || package.CanonicalVarFileId == varFile.Id;
            if (package.CanonicalVarFileId is null)
                package.CanonicalVarFileId = varFile.Id;

            if (isCanonical)
                await ReplaceContentCountsAsync(package.Id, upsert.ContentCounts, cancellationToken).ConfigureAwait(false);

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return package?.Id;
    }

    public async Task<int> RemoveVarFilesAsync(IReadOnlyCollection<long> varFileIds, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(varFileIds);
        if (varFileIds.Count == 0)
            return 0;

        // Packages that may need a new canonical after removal.
        var affectedPackages = await db.VarFiles
            .Where(v => varFileIds.Contains(v.Id) && v.PackageId != null)
            .Select(v => v.PackageId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toRemove = await db.VarFiles
            .Where(v => varFileIds.Contains(v.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.VarFiles.RemoveRange(toRemove);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Re-elect canonicals for packages whose canonical was removed (FK already nulled the pointer).
        foreach (var packageId in affectedPackages)
        {
            var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
            if (package is null || package.CanonicalVarFileId is not null)
                continue;

            var replacement = await db.VarFiles
                .Where(v => v.PackageId == packageId)
                .OrderBy(v => v.IntegrityStatus) // Ok (0) first
                .ThenBy(v => v.Id)
                .Select(v => (long?)v.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            package.CanonicalVarFileId = replacement;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return toRemove.Count;
    }

    public async Task RefreshReadModelAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(packageIds);
        var ids = packageIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        // Batch the whole refresh in one transaction, deferring FTS maintenance so the search index is
        // rebuilt once at the end rather than DELETE+INSERT per row inside its own implicit transaction. (1.25)
        var deferredFts = new List<(long Id, string Blob)>(ids.Count);
        var ownTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownTransaction
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            foreach (var packageId in ids)
                await RefreshOneAsync(packageId, deferredFts, cancellationToken).ConfigureAwait(false);

            await RebuildSearchIndexAsync(deferredFts, cancellationToken).ConfigureAwait(false);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Apply all deferred FTS rows for a bulk refresh in a single pass (inside the caller's transaction).
    private async Task RebuildSearchIndexAsync(List<(long Id, string Blob)> entries, CancellationToken cancellationToken)
    {
        foreach (var (id, blob) in entries)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM PackageSearch WHERE rowid = {0};", [id], cancellationToken).ConfigureAwait(false);
            if (blob is not null)
                await db.Database.ExecuteSqlRawAsync("INSERT INTO PackageSearch(rowid, Blob) VALUES ({0}, {1});", [id, blob], cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshOneAsync(long packageId, List<(long Id, string Blob)> deferredFts, CancellationToken cancellationToken)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
        var item = await db.PackageListItems.FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken).ConfigureAwait(false);

        if (package is null)
        {
            if (item is not null)
                db.PackageListItems.Remove(item);
            deferredFts.Add((packageId, null!)); // null blob → delete-only in the rebuild
            return;
        }

        var varFiles = await db.VarFiles
            .Where(v => v.PackageId == packageId)
            .Select(v => new { v.Id, v.SizeBytes, v.RepositoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var onlineRepoIds = await db.Repositories
            .Where(r => r.IsOnline)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var onlineSet = onlineRepoIds.ToHashSet();

        var total = varFiles.Count;
        var online = varFiles.Count(v => onlineSet.Contains(v.RepositoryId));
        var canonicalSize = package.CanonicalVarFileId is { } cid
            ? varFiles.FirstOrDefault(v => v.Id == cid)?.SizeBytes ?? varFiles.FirstOrDefault()?.SizeBytes ?? 0
            : varFiles.FirstOrDefault()?.SizeBytes ?? 0;

        var counts = await db.PackageContentCounts
            .Where(c => c.PackageId == packageId)
            .ToDictionaryAsync(c => c.Type, c => c.Count, cancellationToken)
            .ConfigureAwait(false);
        var primary = ContentClassificationEngine.ChoosePrimary(counts);

        var stat = await db.UsageStats.FirstOrDefaultAsync(s => s.PackageId == packageId, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            item = new PackageListItem { PackageId = packageId, AddedAt = package.FirstSeenAt };
            db.PackageListItems.Add(item);
        }

        item.VarName = package.VarName;
        item.Creator = package.Creator;
        item.PackageName = package.PackageName;
        item.VersionToken = package.VersionToken;
        item.PrimaryType = primary;
        item.TotalSize = canonicalSize;
        item.TotalInstanceCount = total;
        item.OnlineInstanceCount = online;
        item.IsSingleCopy = online <= 1;
        item.IsFavorite = package.IsFavorite;
        item.Class = stat?.Class ?? ContentClass.Cold;
        item.LastUsedAt = stat?.LastUsedAt;
        item.HasMissingDeps = false; // dependency resolution is Slice 2

        // Defer FTS maintenance to the batched rebuild at the end of the transaction. (1.25)
        deferredFts.Add((package.Id, BuildSearchBlob(package, counts)));
    }

    // The FTS5 search blob for a package: creator + names + content-type words (CJK-safe, trigram-tokenized).
    private static string BuildSearchBlob(Package package, IReadOnlyDictionary<ContentType, int> counts)
    {
        var typeWords = string.Join(' ', counts.Where(c => c.Value > 0).Select(c => c.Key.ToString()));
        return $"{package.Creator} {package.PackageName} {package.VarName} {typeWords}".Trim();
    }

    private async Task ReplaceContentItemsAsync(long varFileId, IReadOnlyList<UpsertContentItem> items, CancellationToken cancellationToken)
    {
        var existing = await db.ContentItems.Where(c => c.VarFileId == varFileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
            db.ContentItems.RemoveRange(existing);

        foreach (var i in items)
        {
            db.ContentItems.Add(new ContentItem
            {
                VarFileId = varFileId,
                Type = i.Type,
                EntryPath = i.EntryPath,
                IsPreset = i.IsPreset,
                Gender = i.Gender,
                GenderConfidence = i.GenderConfidence,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReplaceDependenciesAsync(long varFileId, IReadOnlyList<string> metaRefs, IReadOnlyList<string> embeddedRefs, CancellationToken cancellationToken)
    {
        var existing = await db.Dependencies.Where(d => d.VarFileId == varFileId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
            db.Dependencies.RemoveRange(existing);

        // Meta refs first so a ref present in both keeps RefKind.Meta (the UNIQUE key dedups the rest).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddRefs(varFileId, metaRefs, RefKind.Meta, seen);
        AddRefs(varFileId, embeddedRefs, RefKind.Embedded, seen);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddRefs(long varFileId, IReadOnlyList<string> refsRaw, RefKind kind, HashSet<string> seen)
    {
        foreach (var raw in refsRaw)
        {
            var key = IdentityFold.Compute(raw);
            if (key.Length == 0 || !seen.Add(key))
                continue; // dedup by folded key (satisfies UNIQUE(VarFileId, DependsOnRefKey))

            db.Dependencies.Add(new Dependency
            {
                VarFileId = varFileId,
                DependsOnRefKey = key,
                DependsOnRefRaw = raw,
                RefKind = kind,
                IsMissing = true, // resolved by the dependency resolver
            });
        }
    }

    private async Task ReplaceContentCountsAsync(long packageId, IReadOnlyDictionary<ContentType, int> counts, CancellationToken cancellationToken)
    {
        var existing = await db.PackageContentCounts.Where(c => c.PackageId == packageId).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
            db.PackageContentCounts.RemoveRange(existing);

        foreach (var (type, count) in counts)
        {
            if (count <= 0)
                continue;
            db.PackageContentCounts.Add(new PackageContentCount { PackageId = packageId, Type = type, Count = count });
        }
    }
}
