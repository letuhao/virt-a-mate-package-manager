using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Content;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Pass-2 preview extraction. For each package it locates the canonical var on disk, finds a
/// preview-worthy content entry with a sibling <c>.jpg</c>, stores the image in the
/// <see cref="IThumbnailStore"/>, and stamps <c>PackageListItem.PreviewThumbRef</c>. Runs after pass-1
/// has made the catalog browsable. (Checklist 1.23/1.32.)
/// </summary>
public sealed class EfPreviewIndexer(
    VarVaultDbContext db,
    PreviewExtractor extractor,
    IThumbnailStore thumbnails) : IPreviewIndexer
{
    public async Task<int> BuildPreviewsAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(packageIds);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var activity = Telemetry.StartActivity("index.previews");
        var stored = 0;
        foreach (var packageId in packageIds.Distinct())
        {
            if (await BuildOneAsync(packageId, cancellationToken).ConfigureAwait(false))
                stored++;
        }

        if (stored > 0)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Telemetry.PreviewsExtracted.Add(stored);
        Telemetry.PreviewDurationMs.Record(sw.Elapsed.TotalMilliseconds);
        return stored;
    }

    private async Task<bool> BuildOneAsync(long packageId, CancellationToken cancellationToken)
    {
        var package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken)
            .ConfigureAwait(false);
        if (package?.CanonicalVarFileId is not { } canonicalId)
            return false;

        var varFile = await db.VarFiles
            .FirstOrDefaultAsync(v => v.Id == canonicalId, cancellationToken)
            .ConfigureAwait(false);
        if (varFile is null)
            return false;

        var repo = await db.Repositories
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == varFile.RepositoryId, cancellationToken)
            .ConfigureAwait(false);
        if (repo is null)
            return false;

        var varPath = Path.Combine(repo.MountPath, varFile.RelativePath);

        // Preview-worthy content entries of the canonical var, in DB order.
        var entries = await db.ContentItems
            .Where(c => c.VarFileId == canonicalId)
            .Select(c => new { c.Type, c.EntryPath })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var entry in entries.Where(e => PreviewRules.HasPreview(e.Type)))
        {
            var bytes = await extractor.ExtractAsync(varPath, entry.EntryPath, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
                continue;

            await thumbnails.PutAsync(packageId, bytes, cancellationToken).ConfigureAwait(false);
            Telemetry.PreviewBytesStored.Record(bytes.Length);
            var item = await db.PackageListItems
                .FirstOrDefaultAsync(x => x.PackageId == packageId, cancellationToken)
                .ConfigureAwait(false);
            if (item is not null)
                item.PreviewThumbRef = $"thumb:{packageId}";
            return true;
        }

        return false;
    }
}
