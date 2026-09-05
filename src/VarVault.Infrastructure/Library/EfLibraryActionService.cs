using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Dedup;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Threading;

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
    VarVault.Sdk.Threading.IWriteQueue writeQueue,
    IActivationService activation,
    IProfileService profiles) : ILibraryActionService
{
    private readonly ActivePresetActivationHelper _active = new(db, presets, activation, profiles);
    public async Task<BulkActionResult> AddToPresetAsync(long presetId, IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default)
    {
        var names = await db.Packages.Where(p => packageIds.Contains(p.Id)).Select(p => p.VarName)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int ok = 0, fail = 0;
        foreach (var name in names)
            if ((await presets.AddMemberAsync(presetId, name, cancellationToken).ConfigureAwait(false)).IsSuccess) ok++; else fail++;
        return new BulkActionResult(ok, fail);
    }

    public async Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default) =>
        await health.FixManyAsync(varFileIds, progress: null, cancellationToken).ConfigureAwait(false);

    public async Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default)
    {
        // Load only the selected candidates' identity groups so the predicate can confirm a surviving verified copy.
        var identities = await db.VarFiles.AsNoTracking()
            .Where(v => varFileIds.Contains(v.Id) && v.PackageId != null)
            .Select(v => v.Package!.IdentityKey)
            .Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.VarFiles
            .AsNoTracking()
            .Where(v => v.PackageId != null && identities.Contains(v.Package!.IdentityKey))
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

    public Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> varFileIds, string subfolder, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
            return Task.FromResult(new BulkActionResult(0, varFileIds.Count));

        return writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var rows = await db.VarFiles
                .Where(v => varFileIds.Contains(v.Id))
                .Select(v => new { Entity = v, Mount = v.Repository!.MountPath })
                .ToListAsync(ct).ConfigureAwait(false);
            var byId = rows.ToDictionary(r => r.Entity.Id);
            var moved = new List<(string Source, string Destination)>();
            int ok = 0, fail = 0;
            foreach (var id in varFileIds)
            {
                if (!byId.TryGetValue(id, out var row)) { fail++; continue; }
                try
                {
                    var oldRelativePath = row.Entity.RelativePath;
                    var fileName = Path.GetFileName(oldRelativePath);
                    var newRel = Path.Combine(subfolder, fileName);
                    var src = Path.Combine(row.Mount, oldRelativePath);
                    var dst = Path.Combine(row.Mount, newRel);
                    if (!File.Exists(src)) { fail++; continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Move(src, dst, overwrite: false);
                    moved.Add((src, dst));
                    row.Entity.RelativePath = newRel;
                    ok++;
                }
                catch (IOException) { fail++; }
                catch (UnauthorizedAccessException) { fail++; }
            }
            if (ok == 0)
                return new BulkActionResult(0, fail);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return new BulkActionResult(ok, fail);
            }
            catch (DbUpdateException)
            {
                foreach (var move in moved.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(move.Destination) && !File.Exists(move.Source))
                            File.Move(move.Destination, move.Source);
                    }
                    catch (IOException)
                    {
                        // The catalog write failed; leave the filesystem failure visible for a later rescan.
                    }
                }
                db.ChangeTracker.Clear();
                return new BulkActionResult(0, fail + ok);
            }
        }, WritePriority.Interactive, cancellationToken);
    }

    public Task<bool> SetFavoriteAsync(long packageId, bool isFavorite, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, ct).ConfigureAwait(false);
            if (pkg is null)
                return false;
            pkg.IsFavorite = isFavorite;
            var item = await db.PackageListItems.FirstOrDefaultAsync(i => i.PackageId == packageId, ct).ConfigureAwait(false);
            if (item is not null)
                item.IsFavorite = isFavorite;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }, WritePriority.Interactive, cancellationToken);

    public Task<BulkActionResult> SetFavoritesAsync(IReadOnlyList<long> packageIds, bool isFavorite, CancellationToken cancellationToken = default) =>
        writeQueue.EnqueueScopedAsync(async (sp, ct) =>
        {
            var db = sp.GetRequiredService<VarVaultDbContext>();
            if (packageIds.Count == 0)
                return new BulkActionResult(0, 0);
            var ids = packageIds.Distinct().ToList();
            var packages = await db.Packages.Where(p => ids.Contains(p.Id)).ToListAsync(ct).ConfigureAwait(false);
            var items = await db.PackageListItems.Where(i => ids.Contains(i.PackageId)).ToListAsync(ct).ConfigureAwait(false);
            foreach (var pkg in packages)
                pkg.IsFavorite = isFavorite;
            foreach (var item in items)
                item.IsFavorite = isFavorite;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new BulkActionResult(packages.Count, ids.Count - packages.Count);
        }, cancellationToken: cancellationToken);

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

    public Task<MissingLogActivation> InstallIntoActiveProfileAsync(
        IReadOnlyList<string> varNames, CancellationToken cancellationToken = default) =>
        _active.ActivateAsync(varNames, cancellationToken);

    public Task<MissingLogActivation> UninstallFromActiveProfileAsync(
        IReadOnlyList<string> varNames, CancellationToken cancellationToken = default) =>
        _active.UninstallAsync(varNames, cancellationToken);
}
