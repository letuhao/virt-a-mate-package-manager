using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Coordinates duplicate-entry repair into the catalog: writes <c>.dedup.var</c>, indexes it,
/// supersedes the original (retained). Mirrors <see cref="EncodingFixCoordinator"/>.
/// </summary>
public sealed class DuplicateEntryFixCoordinator(
    VarVaultDbContext db,
    IDuplicateEntryFixer fixer,
    IVarInspector inspector,
    IClock clock)
{
    public async Task<Result<long>> FixAsync(
        long brokenVarFileId,
        string outputPath,
        IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(outputPath);

        var broken = await db.VarFiles.FirstOrDefaultAsync(v => v.Id == brokenVarFileId, cancellationToken).ConfigureAwait(false);
        if (broken is null)
            return Result.Failure<long>("dedup.missing", "Var file not found.");
        if (broken.SupersededByVarFileId is not null)
            return Result.Failure<long>("dedup.superseded", "Var already has a repaired sibling — original retained.");

        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == broken.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return Result.Failure<long>("dedup.norepo", "Repository not found.");

        var sourcePath = Path.Combine(repo.MountPath, broken.RelativePath);
        if (!File.Exists(sourcePath))
            return Result.Failure<long>("dedup.missing", "Source file missing on disk.");

        // Confirm collisions (catalog may be stale).
        var before = inspector.Inspect(sourcePath, cancellationToken);
        if (before.IsFailure)
            return Result.Failure<long>(before.Error);
        var dups = VamLoadDefectDetector.Detect(before.Value.Entries)
            .Where(d => d.Kind == VamLoadDefectKind.DuplicateEntries)
            .ToList();
        if (dups.Count == 0)
            return Result.Failure<long>("dedup.notneeded", "No duplicate entry paths detected.");

        var fix = await fixer.FixAsync(sourcePath, outputPath, keepByNormalizedKey, cancellationToken)
            .ConfigureAwait(false);
        if (fix.IsFailure)
            return Result.Failure<long>(fix.Error);

        var inspection = inspector.Inspect(outputPath, cancellationToken);
        if (inspection.IsFailure)
        {
            TryDelete(outputPath);
            return Result.Failure<long>("dedup.inspect", inspection.Error.Message);
        }

        var signatures = inspection.Value.Signatures;
        var encoding = inspection.Value.Encoding;
        var now = clock.UtcNow.UtcDateTime;
        var info = new FileInfo(outputPath);

        var fixedVar = new VarFile
        {
            PackageId = broken.PackageId,
            RepositoryId = broken.RepositoryId,
            RelativePath = Path.GetRelativePath(repo.MountPath, outputPath),
            SizeBytes = info.Exists ? info.Length : 0,
            FileMtime = info.Exists ? info.LastWriteTimeUtc : now,
            ContentSignature = signatures?.ContentSignature,
            PayloadSignature = signatures?.PayloadSignature,
            ContentSignatureNoPath = signatures?.ContentSignatureNoPath,
            EncodingHealth = encoding?.Health ?? EncodingHealth.Ok,
            DetectedCodepage = encoding?.DetectedCodepage ?? broken.DetectedCodepage,
            IntegrityStatus = inspection.Value.Integrity,
            FixedFromVarFileId = broken.Id,
            IndexedAt = now,
        };
        db.VarFiles.Add(fixedVar);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        broken.SupersededByVarFileId = fixedVar.Id;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (broken.PackageId is { } packageId)
        {
            var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
            if (package is not null
                && (package.CanonicalVarFileId is null || package.CanonicalVarFileId == broken.Id))
            {
                package.CanonicalVarFileId = fixedVar.Id;
            }

            var item = await db.PackageListItems.FirstOrDefaultAsync(i => i.PackageId == packageId, cancellationToken)
                .ConfigureAwait(false);
            if (item is not null)
            {
                var copies = await db.VarFiles
                    .Where(v => v.PackageId == packageId)
                    .Select(v => new { v.Id, v.SizeBytes, v.RepositoryId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var onlineRepos = await db.Repositories
                    .Where(r => r.IsOnline)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                var onlineSet = onlineRepos.ToHashSet();
                item.TotalInstanceCount = copies.Count;
                item.OnlineInstanceCount = copies.Count(c => onlineSet.Contains(c.RepositoryId));
                item.IsSingleCopy = item.OnlineInstanceCount <= 1;
                var canonicalId = package?.CanonicalVarFileId ?? fixedVar.Id;
                item.TotalSize = copies.FirstOrDefault(c => c.Id == canonicalId)?.SizeBytes
                                 ?? copies.FirstOrDefault()?.SizeBytes
                                 ?? item.TotalSize;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return fixedVar.Id;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
