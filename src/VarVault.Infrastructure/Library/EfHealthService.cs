using System.IO;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N5 · Health facade. Encoding-fix groups (by detected codepage) and integrity issues from the
/// catalog; <see cref="FixAsync"/> delegates to <see cref="EncodingFixCoordinator"/> which writes a new
/// UTF-8 var, validates it, and records lineage (original retained). Live VaM-load scan reopens packages
/// to catch duplicate entry keys that crash <c>FileManager.RegisterPackage</c>.
/// </summary>
public sealed class EfHealthService(
    VarVaultDbContext db,
    EncodingFixCoordinator coordinator,
    IVarInspector? inspector = null,
    IWriteQueue? writeQueue = null,
    DuplicateEntryFixCoordinator? dedupCoordinator = null) : IHealthService
{
    public async Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken cancellationToken = default)
    {
        // Superseded originals stay on disk for recovery but must not inflate "still needs fix" counts.
        var groups = await db.VarFiles.AsNoTracking()
            .Where(v => (v.EncodingHealth == EncodingHealth.NeedsFix || v.EncodingHealth == EncodingHealth.PartiallyBroken)
                        && v.DetectedCodepage != null
                        && v.SupersededByVarFileId == null)
            .GroupBy(v => v.DetectedCodepage!)
            .Select(g => new EncodingGroup(g.Key, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return groups;
    }

    public Task<PageResult<IntegrityIssue>> IntegrityPageAsync(PageRequest request, CancellationToken cancellationToken = default) =>
        PageIssuesAsync(
            v => v.IntegrityStatus == IntegrityStatus.CorruptZip
                 || v.IntegrityStatus == IntegrityStatus.DuplicateEntries,
            request,
            cancellationToken);

    public async Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken cancellationToken = default)
    {
        return (await IntegrityPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }

    public Task<PageResult<IntegrityIssue>> MissingMetaPageAsync(PageRequest request, CancellationToken cancellationToken = default) =>
        PageIssuesAsync(v => v.IntegrityStatus == IntegrityStatus.MissingMeta, request, cancellationToken);

    public async Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken cancellationToken = default)
    {
        return (await MissingMetaPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }

    public async Task<IReadOnlyList<VamLoadIssue>> ScanVamLoadAsync(
        VamLoadScanScope scope,
        IProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress ??= IProgressSink.Null;
        Guard.NotNull(inspector);
        Guard.NotNull(writeQueue);
        var targets = await LoadScanTargetsAsync(scope, cancellationToken).ConfigureAwait(false);
        var issues = new List<VamLoadIssue>();
        var statusUpdates = new Dictionary<long, IntegrityStatus>();

        for (var i = 0; i < targets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = targets[i];
            progress.Report(new ProgressReport(i, targets.Count, $"VaM-load scan {i + 1}/{targets.Count}: {t.VarName}"));

            if (!File.Exists(t.FullPath))
            {
                issues.Add(Issue(t, VamLoadDefectKind.CorruptZip, "File missing on disk."));
                statusUpdates[t.VarFileId] = IntegrityStatus.CorruptZip;
                continue;
            }

            var inspect = inspector.Inspect(t.FullPath, cancellationToken);
            if (!inspect.IsSuccess)
            {
                issues.Add(Issue(t, VamLoadDefectKind.CorruptZip, inspect.Error.Message));
                statusUpdates[t.VarFileId] = IntegrityStatus.CorruptZip;
                continue;
            }

            var inspection = inspect.Value;
            var zipOpenFailed = false;
            string? zipOpenError = null;
            try
            {
                using var archive = ZipFile.OpenRead(t.FullPath);
                _ = archive.Entries.Count;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException)
            {
                zipOpenFailed = true;
                zipOpenError = $"{ex.GetType().Name}: {ex.Message}";
            }

            var encoding = inspection.Encoding?.Health ?? EncodingHealth.Unknown;
            var defects = VamLoadDefectDetector.Detect(
                inspection.Entries,
                inspection.Integrity,
                encoding,
                zipOpenFailed,
                zipOpenError);

            // VaM-load tab is for RegisterPackage killers only — Encoding tab owns NeedsFix.
            var structural = defects
                .Where(d => d.Kind is not VamLoadDefectKind.EncodingNeedsFix)
                .ToList();

            foreach (var d in structural)
                issues.Add(Issue(t, d.Kind, d.Detail));

            var suggested = VamLoadDefectDetector.SuggestedIntegrityStatus(defects);
            if (suggested is { } status)
                statusUpdates[t.VarFileId] = status;
            else if (t.IntegrityStatus == IntegrityStatus.DuplicateEntries)
                statusUpdates[t.VarFileId] = IntegrityStatus.Ok; // cleared by clean re-scan
        }

        if (statusUpdates.Count > 0)
        {
            await writeQueue.EnqueueAsync(async ct =>
            {
                var ids = statusUpdates.Keys.ToList();
                var rows = await db.VarFiles.Where(v => ids.Contains(v.Id)).ToListAsync(ct).ConfigureAwait(false);
                foreach (var row in rows)
                {
                    if (!statusUpdates.TryGetValue(row.Id, out var next))
                        continue;
                    // CorruptZip → DuplicateEntries is allowed when the CD confirms collisions
                    // (ZipOpenFailed was often a symptom). Still block CorruptZip → Ok.
                    if (row.IntegrityStatus == IntegrityStatus.CorruptZip
                        && next == IntegrityStatus.Ok)
                        continue;
                    row.IntegrityStatus = next;
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        progress.Report(new ProgressReport(targets.Count, Math.Max(targets.Count, 1),
            $"VaM-load scan done · {issues.Count} finding(s) in {targets.Count} package(s)"));
        return issues;
    }

    public async Task<Result<long>> FixAsync(long varFileId, CancellationToken cancellationToken = default)
    {
        var v = await db.VarFiles
            .Where(x => x.Id == varFileId)
            .Select(x => new { x.RelativePath, x.Repository!.MountPath })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (v is null)
            return Result.Failure<long>("fix.missing", "Var file not found.");

        var sourcePath = Path.Combine(v.MountPath, v.RelativePath);
        // New sibling: "<name>.var" → "<name>.fixed.var".
        var outputPath = sourcePath.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
            ? sourcePath[..^4] + ".fixed.var"
            : sourcePath + ".fixed.var";

        return await coordinator.FixAsync(varFileId, outputPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<long>> FixDuplicateEntriesAsync(
        long varFileId,
        IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
        CancellationToken cancellationToken = default)
    {
        if (dedupCoordinator is null)
            return Result.Failure<long>("dedup.unavailable", "Duplicate-entry fixer is not registered.");

        var v = await db.VarFiles
            .Where(x => x.Id == varFileId)
            .Select(x => new { x.RelativePath, x.Repository!.MountPath })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (v is null)
            return Result.Failure<long>("dedup.missing", "Var file not found.");

        var sourcePath = Path.Combine(v.MountPath, v.RelativePath);
        var outputPath = sourcePath.EndsWith(".var", StringComparison.OrdinalIgnoreCase)
            ? sourcePath[..^4] + ".dedup.var"
            : sourcePath + ".dedup.var";

        return await dedupCoordinator.FixAsync(varFileId, outputPath, keepByNormalizedKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<DuplicateEntryCollision>>> GetDuplicateEntryCollisionsAsync(
        long varFileId,
        CancellationToken cancellationToken = default)
    {
        if (inspector is null)
            return Result.Failure<IReadOnlyList<DuplicateEntryCollision>>("dedup.unavailable", "Inspector is not registered.");

        var v = await db.VarFiles
            .Where(x => x.Id == varFileId)
            .Select(x => new { x.RelativePath, x.Repository!.MountPath })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (v is null)
            return Result.Failure<IReadOnlyList<DuplicateEntryCollision>>("dedup.missing", "Var file not found.");

        var sourcePath = Path.Combine(v.MountPath, v.RelativePath);
        if (!File.Exists(sourcePath))
            return Result.Failure<IReadOnlyList<DuplicateEntryCollision>>("dedup.missing", "Source file missing on disk.");

        var insp = inspector.Inspect(sourcePath, cancellationToken);
        if (insp.IsFailure)
            return Result.Failure<IReadOnlyList<DuplicateEntryCollision>>(insp.Error);

        var groups = VamLoadDefectDetector.ListCollisions(insp.Value.Entries);
        IReadOnlyList<DuplicateEntryCollision> mapped = groups
            .Select(g => new DuplicateEntryCollision(
                g.NormalizedKey,
                g.Members.Select(m => new DuplicateEntryCandidate(m.FullName, m.UncompressedSize, m.Crc32)).ToList(),
                g.SuggestedKeepFullName))
            .ToList();
        return Result.Success(mapped);
    }

    public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken cancellationToken = default) =>
        FixGroupAsync(codepageFilter, IProgressSink.Null, cancellationToken);

    public async Task<BulkActionResult> FixGroupAsync(string? codepageFilter, IProgressSink progress, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(progress);
        var query = db.VarFiles.AsNoTracking()
            .Where(v => (v.EncodingHealth == EncodingHealth.NeedsFix || v.EncodingHealth == EncodingHealth.PartiallyBroken)
                        && v.DetectedCodepage != null
                        && v.SupersededByVarFileId == null);

        if (!string.IsNullOrWhiteSpace(codepageFilter))
        {
            var filter = codepageFilter.Trim();
            query = query.Where(v =>
                v.DetectedCodepage == filter
                || v.DetectedCodepage!.Contains(filter));
        }

        var ids = await query
            .OrderBy(v => v.Id)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await FixManyAsync(ids, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BulkActionResult> FixManyAsync(
        IReadOnlyList<long> varFileIds,
        IProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(varFileIds);
        progress ??= IProgressSink.Null;
        var ids = varFileIds.Where(id => id > 0).Distinct().ToList();
        int ok = 0, fail = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new ProgressReport(i, ids.Count, $"Fixing {i + 1} of {ids.Count}"));
            if ((await FixAsync(ids[i], cancellationToken).ConfigureAwait(false)).IsSuccess)
                ok++;
            else
                fail++;
        }

        progress.Report(new ProgressReport(ids.Count, Math.Max(ids.Count, 1),
            $"Done · {ok} fixed · originals retained"));
        return new BulkActionResult(ok, fail);
    }

    public async Task<BulkActionResult> FixDuplicateEntriesManyAsync(
        IReadOnlyList<long> varFileIds,
        IProgressSink? progress = null,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(varFileIds);
        progress ??= IProgressSink.Null;
        var ids = varFileIds.Where(id => id > 0).Distinct().ToList();
        int ok = 0, fail = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new ProgressReport(i, ids.Count, $"Dedup-fixing {i + 1} of {ids.Count}"));
            if ((await FixDuplicateEntriesAsync(ids[i], cancellationToken: cancellationToken).ConfigureAwait(false)).IsSuccess)
                ok++;
            else
                fail++;
        }

        progress.Report(new ProgressReport(ids.Count, Math.Max(ids.Count, 1),
            $"Done · {ok} dedup-fixed · originals retained"));
        return new BulkActionResult(ok, fail);
    }

    private async Task<IReadOnlyList<ScanTarget>> LoadScanTargetsAsync(
        VamLoadScanScope scope,
        CancellationToken cancellationToken)
    {
        if (scope == VamLoadScanScope.InstalledOnly)
        {
            var rows = await (
                    from link in db.ProfilePackageLinks.AsNoTracking()
                    join profile in db.Profiles.AsNoTracking() on link.ProfileId equals profile.Id
                    where profile.IsActive
                    join pkg in db.Packages.AsNoTracking() on link.PackageId equals pkg.Id
                    where pkg.CanonicalVarFileId != null
                    join vf in db.VarFiles.AsNoTracking() on pkg.CanonicalVarFileId equals vf.Id
                    join repo in db.Repositories.AsNoTracking() on vf.RepositoryId equals repo.Id
                    orderby link.InstalledAt descending
                    select new
                    {
                        vf.Id,
                        PackageId = (long?)pkg.Id,
                        pkg.VarName,
                        vf.RelativePath,
                        repo.MountPath,
                        InstalledAt = (DateTime?)link.InstalledAt,
                        vf.IntegrityStatus,
                        vf.EncodingHealth,
                    })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.Select(r => new ScanTarget(
                r.Id, r.PackageId, r.VarName, r.RelativePath,
                Path.Combine(r.MountPath, r.RelativePath),
                r.InstalledAt, r.IntegrityStatus, r.EncodingHealth)).ToList();
        }

        var catalog = await (
                from vf in db.VarFiles.AsNoTracking()
                where vf.SupersededByVarFileId == null
                join repo in db.Repositories.AsNoTracking() on vf.RepositoryId equals repo.Id
                select new
                {
                    vf.Id,
                    vf.PackageId,
                    vf.RelativePath,
                    repo.MountPath,
                    vf.IntegrityStatus,
                    vf.EncodingHealth,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var packageIds = catalog.Where(c => c.PackageId is long).Select(c => c.PackageId!.Value).Distinct().ToList();
        var names = packageIds.Count == 0
            ? new Dictionary<long, string>()
            : await db.Packages.AsNoTracking()
                .Where(p => packageIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.VarName, cancellationToken)
                .ConfigureAwait(false);

        var installed = await (
                from link in db.ProfilePackageLinks.AsNoTracking()
                join profile in db.Profiles.AsNoTracking() on link.ProfileId equals profile.Id
                where profile.IsActive
                select new { link.PackageId, link.InstalledAt })
            .ToDictionaryAsync(x => x.PackageId, x => x.InstalledAt, cancellationToken)
            .ConfigureAwait(false);

        return catalog
            .Select(c =>
            {
                long? pkgId = c.PackageId;
                DateTime? installedAt = pkgId is long id && installed.TryGetValue(id, out var at) ? at : null;
                var name = pkgId is long pid && names.TryGetValue(pid, out var vn) ? vn : c.RelativePath;
                return new ScanTarget(
                    c.Id, pkgId, name, c.RelativePath,
                    Path.Combine(c.MountPath, c.RelativePath),
                    installedAt, c.IntegrityStatus, c.EncodingHealth);
            })
            .OrderBy(t => t.InstalledAt is null)
            .ThenByDescending(t => t.InstalledAt)
            .ThenBy(t => t.VarFileId)
            .ToList();
    }

    private static VamLoadIssue Issue(ScanTarget t, VamLoadDefectKind kind, string detail) =>
        new(t.VarFileId, t.PackageId, t.VarName, t.RelativePath, t.FullPath, t.InstalledAt, kind.ToString(), detail);

    private async Task<PageResult<IntegrityIssue>> PageIssuesAsync(
        System.Linq.Expressions.Expression<Func<VarFile, bool>> predicate,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var page = request.Normalize();
        var query = db.VarFiles.AsNoTracking().Where(predicate);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(v => v.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(v => new IntegrityIssue(
                v.Id,
                v.Package != null ? v.Package.VarName : v.RelativePath,
                v.RelativePath,
                v.IntegrityStatus.ToString()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PageResult<IntegrityIssue>(items, total, page.SafePageNumber, page.SafePageSize);
    }

    private sealed record ScanTarget(
        long VarFileId,
        long? PackageId,
        string VarName,
        string RelativePath,
        string FullPath,
        DateTime? InstalledAt,
        IntegrityStatus IntegrityStatus,
        EncodingHealth EncodingHealth);
}
