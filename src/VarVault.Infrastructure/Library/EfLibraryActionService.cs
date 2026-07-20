using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dedup;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N10 · Bulk library actions composing existing facades: add-to-preset (<see cref="IPresetService"/>),
/// fix-encoding (<see cref="IHealthService"/>), delete (gated by <see cref="DeletionPredicate"/> → trash),
/// export-txt. (16-checklist BE-N10.)
/// </summary>
public sealed class EfLibraryActionService(
    VarVaultDbContext db,
    IPresetService presets,
    IHealthService health,
    ITrashService trash,
    VarVault.Sdk.Threading.IWriteQueue writeQueue) : ILibraryActionService
{
    public async Task<BulkActionResult> AddToPresetAsync(long presetId, IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default)
    {
        var names = await db.Packages.Where(p => packageIds.Contains(p.Id)).Select(p => p.VarName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int ok = 0, fail = 0;
        foreach (var name in names)
            if ((await presets.AddMemberAsync(presetId, name, cancellationToken).ConfigureAwait(false)).IsSuccess) ok++; else fail++;
        return new BulkActionResult(ok, fail);
    }

    public async Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default)
    {
        int ok = 0, fail = 0;
        foreach (var id in varFileIds)
            if ((await health.FixAsync(id, cancellationToken).ConfigureAwait(false)).IsSuccess) ok++; else fail++;
        return new BulkActionResult(ok, fail);
    }

    public async Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default)
    {
        // Load candidates + their identity groups so the predicate can confirm a surviving verified copy.
        var rows = await db.VarFiles
            .Where(v => v.PackageId != null)
            .Select(v => new
            {
                v.Id, v.Package!.IdentityKey, v.ContentHash, v.RelativePath,
                v.Repository!.IsOnline, v.Repository.MountPath,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var factsByIdentity = rows
            .GroupBy(r => r.IdentityKey)
            .ToDictionary(g => g.Key, g => g.Select(r => new VarFileDedupFacts(r.Id, r.IdentityKey, null, r.ContentHash, r.IsOnline)).ToList());
        var byId = rows.ToDictionary(r => r.Id);

        int ok = 0, fail = 0;
        foreach (var id in varFileIds)
        {
            if (!byId.TryGetValue(id, out var row)) { fail++; continue; }
            var candidate = new VarFileDedupFacts(row.Id, row.IdentityKey, null, row.ContentHash, row.IsOnline);
            var verdict = DeletionPredicate.Evaluate(candidate, factsByIdentity[row.IdentityKey]);
            if (!verdict.CanDelete) { fail++; continue; }
            var path = Path.Combine(row.MountPath, row.RelativePath);
            if ((await trash.TrashAsync(path, "deleted from library", cancellationToken).ConfigureAwait(false)).IsSuccess) ok++; else fail++;
        }
        return new BulkActionResult(ok, fail);
    }

    public async Task<string> ExportTxtAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default)
    {
        var names = await db.Packages.Where(p => packageIds.Contains(p.Id))
            .OrderBy(p => p.VarName).Select(p => p.VarName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var n in names)
            sb.AppendLine(n);
        return sb.ToString();
    }

    public async Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> varFileIds, string subfolder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
            return new BulkActionResult(0, varFileIds.Count);

        var rows = await db.VarFiles
            .Where(v => varFileIds.Contains(v.Id))
            .Select(v => new { v.Id, v.RelativePath, Mount = v.Repository!.MountPath })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byId = rows.ToDictionary(r => r.Id);

        int ok = 0, fail = 0;
        foreach (var id in varFileIds)
        {
            if (!byId.TryGetValue(id, out var row)) { fail++; continue; }
            try
            {
                var fileName = Path.GetFileName(row.RelativePath);
                var newRel = Path.Combine(subfolder, fileName);
                var src = Path.Combine(row.Mount, row.RelativePath);
                var dst = Path.Combine(row.Mount, newRel);
                if (!File.Exists(src)) { fail++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Move(src, dst, overwrite: false); // same-volume move within the repo → atomic rename
                var entity = await db.VarFiles.FirstAsync(v => v.Id == id, cancellationToken).ConfigureAwait(false);
                entity.RelativePath = newRel;
                ok++;
            }
            catch (IOException) { fail++; }
            catch (UnauthorizedAccessException) { fail++; }
        }
        // Single-writer discipline (CLAUDE.md): route the catalog write through the write queue. (AC-31)
        await writeQueue.EnqueueAsync(ct => db.SaveChangesAsync(ct), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new BulkActionResult(ok, fail);
    }

    public async Task<bool> SetFavoriteAsync(long packageId, bool isFavorite, CancellationToken cancellationToken = default)
    {
        var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken).ConfigureAwait(false);
        if (pkg is null)
            return false;
        pkg.IsFavorite = isFavorite;
        // Keep the materialized read model in sync so the grid/detail reflect it immediately (no re-index).
        var item = await db.PackageListItems.FirstOrDefaultAsync(i => i.PackageId == packageId, cancellationToken).ConfigureAwait(false);
        if (item is not null)
            item.IsFavorite = isFavorite;
        // Single-writer discipline (CLAUDE.md): route the catalog write through the write queue.
        await writeQueue.EnqueueAsync(ct => db.SaveChangesAsync(ct), cancellationToken: cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<TxtResolveResult> ResolveTxtAsync(string txt, CancellationToken cancellationToken = default)
    {
        var wanted = (txt ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToList();
        if (wanted.Count == 0)
            return new TxtResolveResult([], []);

        var owned = await db.Packages
            .Where(p => wanted.Contains(p.VarName))
            .Select(p => new { p.Id, p.VarName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var matchedNames = owned.Select(o => o.VarName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatched = wanted.Where(w => !matchedNames.Contains(w)).ToList();
        return new TxtResolveResult(owned.Select(o => o.Id).ToList(), unmatched);
    }
}
