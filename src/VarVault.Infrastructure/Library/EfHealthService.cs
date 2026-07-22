using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N5 · Health facade. Encoding-fix groups (by detected codepage) and integrity issues from the
/// catalog; <see cref="FixAsync"/> delegates to <see cref="EncodingFixCoordinator"/> which writes a new
/// UTF-8 var, validates it, and records lineage (original retained). (16-checklist BE-N5.)
/// </summary>
public sealed class EfHealthService(VarVaultDbContext db, EncodingFixCoordinator coordinator) : IHealthService
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
        PageIssuesAsync(IntegrityStatus.CorruptZip, request, cancellationToken);

    public async Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken cancellationToken = default)
    {
        return (await IntegrityPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
    }

    public Task<PageResult<IntegrityIssue>> MissingMetaPageAsync(PageRequest request, CancellationToken cancellationToken = default) =>
        PageIssuesAsync(IntegrityStatus.MissingMeta, request, cancellationToken);

    public async Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken cancellationToken = default)
    {
        return (await MissingMetaPageAsync(new PageRequest(1, 100), cancellationToken).ConfigureAwait(false)).Items;
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

    private async Task<PageResult<IntegrityIssue>> PageIssuesAsync(
        IntegrityStatus status,
        PageRequest request,
        CancellationToken cancellationToken)
    {
        var page = request.Normalize();
        var query = db.VarFiles.AsNoTracking().Where(v => v.IntegrityStatus == status);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(v => v.Id)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(v => new IntegrityIssue(v.Id, v.Package != null ? v.Package.VarName : v.RelativePath, v.RelativePath))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PageResult<IntegrityIssue>(items, total, page.SafePageNumber, page.SafePageSize);
    }
}
