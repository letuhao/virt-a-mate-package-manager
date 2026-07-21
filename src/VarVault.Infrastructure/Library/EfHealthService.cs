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
        var groups = await db.VarFiles
            .Where(v => (v.EncodingHealth == EncodingHealth.NeedsFix || v.EncodingHealth == EncodingHealth.PartiallyBroken)
                        && v.DetectedCodepage != null)
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
