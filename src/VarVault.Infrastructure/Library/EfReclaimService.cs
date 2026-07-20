using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dedup;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// BE-N4 · Reclaim facade. Exact within-identity duplicate groups via <see cref="DedupGrouping"/>; deletes
/// gated by <see cref="DeletionPredicate"/> — a copy is trashed only when a verified, online, hash-equal
/// duplicate of the same identity survives (single-copy protected). Hashes are computed lazily before
/// delete. (16-checklist BE-N4.)
/// </summary>
public sealed class EfReclaimService(VarVaultDbContext db, ITrashService trash, IFileHasher hasher) : IReclaimService
{
    public async Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(cancellationToken).ConfigureAwait(false);
        var facts = rows.Select(r => r.Facts);
        var analysis = DedupGrouping.Analyze(facts);

        var byId = rows.ToDictionary(r => r.Facts.Id);
        return analysis.WithinIdentity
            .Select(g => new DuplicateGroup(g.IdentityKey, g.ContentSignature,
                g.Members.Select(m => byId[m.Id] is var row && row is not null
                    ? new DuplicateCopy(m.Id, row.Tier, row.Path, m.IsOnline, row.Size)
                    : new DuplicateCopy(m.Id, 0, "", m.IsOnline, 0)).ToList()))
            .ToList();
    }

    public async Task<IReadOnlyList<NearDuplicateGroup>> NearDuplicateGroupsAsync(CancellationToken cancellationToken = default)
    {
        // Near-dup = same content payload (content minus meta.json) across ≥2 distinct identities. The payload
        // signature is already computed + stored per var; this is a pure catalog query. (24-checklist A9.)
        var rows = await db.VarFiles
            .Where(v => v.PackageId != null && v.PayloadSignature != null)
            .Select(v => new
            {
                v.Id,
                v.Package!.IdentityKey,
                v.PayloadSignature,
                v.SizeBytes,
                Tier = v.Repository!.Tier,
                v.Repository.IsOnline,
                v.Repository.MountPath,
                v.RelativePath,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.PayloadSignature!, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.IdentityKey).Distinct(StringComparer.Ordinal).Count() >= 2)
            .Select(g => new NearDuplicateGroup(
                g.Key,
                string.Join(", ", g.Select(x => x.IdentityKey).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)),
                g.Select(x => new DuplicateCopy(x.Id, x.Tier, Path.Combine(x.MountPath, x.RelativePath), x.IsOnline, x.SizeBytes)).ToList()))
            .ToList();
    }

    public async Task<ReclaimResult> TrashRedundantAsync(long keepVarFileId, IReadOnlyList<long> trashVarFileIds, CancellationToken cancellationToken = default)
    {
        var rows = await LoadRowsAsync(cancellationToken).ConfigureAwait(false);
        var byId = rows.ToDictionary(r => r.Facts.Id);
        if (!byId.TryGetValue(keepVarFileId, out var keep))
            return new ReclaimResult(0, trashVarFileIds.Count);

        // The identity group (same IdentityKey) — the pool the predicate checks for a surviving copy.
        var group = rows.Where(r => r.Facts.IdentityKey == keep.Facts.IdentityKey).ToList();

        // Ensure hashes are computed (lazy verify-before-delete) for the keep + each candidate.
        await EnsureHashAsync(keep, cancellationToken).ConfigureAwait(false);
        int trashed = 0, blocked = 0;
        foreach (var id in trashVarFileIds)
        {
            if (!byId.TryGetValue(id, out var candidate)) { blocked++; continue; }
            await EnsureHashAsync(candidate, cancellationToken).ConfigureAwait(false);

            var facts = group.Select(r => r.Facts).ToList();
            var verdict = DeletionPredicate.Evaluate(candidate.Facts, facts);
            if (!verdict.CanDelete) { blocked++; continue; }

            var result = await trash.TrashAsync(candidate.Path, "duplicate removed", cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess) trashed++; else blocked++;
        }
        return new ReclaimResult(trashed, blocked);
    }

    private async Task EnsureHashAsync(Row row, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(row.Facts.ContentHash) || !File.Exists(row.Path))
            return;
        var hash = await hasher.ComputeAsync(row.Path, cancellationToken).ConfigureAwait(false);
        if (hash.IsFailure)
            return;
        var vf = await db.VarFiles.FirstAsync(v => v.Id == row.Facts.Id, cancellationToken).ConfigureAwait(false);
        vf.ContentHash = hash.Value;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        row.Facts = row.Facts with { ContentHash = hash.Value };
    }

    private async Task<List<Row>> LoadRowsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.VarFiles
            .Where(v => v.PackageId != null && v.ContentSignature != null)
            .Select(v => new
            {
                v.Id,
                v.Package!.IdentityKey,
                v.ContentSignature,
                v.ContentHash,
                v.SizeBytes,
                Tier = v.Repository!.Tier,
                v.Repository.IsOnline,
                v.Repository.MountPath,
                v.RelativePath,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => new Row(
            new VarFileDedupFacts(r.Id, r.IdentityKey, r.ContentSignature, r.ContentHash, r.IsOnline),
            r.Tier, Path.Combine(r.MountPath, r.RelativePath), r.SizeBytes)).ToList();
    }

    private sealed class Row(VarFileDedupFacts facts, int tier, string path, long size)
    {
        public VarFileDedupFacts Facts { get; set; } = facts;
        public int Tier { get; } = tier;
        public string Path { get; } = path;
        public long Size { get; } = size;
    }
}
