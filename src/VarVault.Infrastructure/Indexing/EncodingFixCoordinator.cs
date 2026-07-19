using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// 🔒⚠ Coordinates an encoding fix into the catalog: fixes a broken var to a new file, indexes the
/// fixed var as a new <see cref="VarFile"/>, and records the lineage — <c>fixed.FixedFromVarFileId</c>
/// and <c>original.SupersededByVarFileId</c>. The original is <b>retained</b> (never trashed here) so
/// the fixed↔original pair reads as lineage, not rival duplicates. (IDX-9; checklist 4.14.)
/// </summary>
public sealed class EncodingFixCoordinator(VarVaultDbContext db, IEncodingFixer fixer, IVarInspector inspector, IClock clock)
{
    public async Task<Result<long>> FixAsync(long brokenVarFileId, string outputPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(outputPath);

        var broken = await db.VarFiles.FirstOrDefaultAsync(v => v.Id == brokenVarFileId, cancellationToken).ConfigureAwait(false);
        if (broken is null)
            return Result.Failure<long>("fix.missing", "Var file not found.");
        if (broken.EncodingHealth is not (EncodingHealth.NeedsFix or EncodingHealth.PartiallyBroken))
            return Result.Failure<long>("fix.nothealthy", "Var is not flagged for an encoding fix.");
        if (string.IsNullOrEmpty(broken.DetectedCodepage))
            return Result.Failure<long>("fix.nocodepage", "No codepage detected — flagged for review.");

        var codePage = EncodingHealthEngine.CodePageFor(broken.DetectedCodepage);
        if (codePage is null)
            return Result.Failure<long>("fix.nocodepage", $"Unknown codepage '{broken.DetectedCodepage}'.");

        var repo = await db.Repositories.FirstOrDefaultAsync(r => r.Id == broken.RepositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return Result.Failure<long>("fix.norepo", "Repository not found.");

        var sourcePath = Path.Combine(repo.MountPath, broken.RelativePath);
        var fix = await fixer.FixAsync(sourcePath, codePage.Value, outputPath, cancellationToken).ConfigureAwait(false);
        if (fix.IsFailure)
            return Result.Failure<long>(fix.Error);

        // Index the fixed var as a new VarFile in the same repo, linked to the original.
        var inspection = inspector.Inspect(outputPath, cancellationToken);
        var signatures = inspection.IsSuccess ? inspection.Value.Signatures : null;
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
            EncodingHealth = EncodingHealth.Fixed,
            IntegrityStatus = IntegrityStatus.Ok,
            FixedFromVarFileId = broken.Id,
            IndexedAt = now,
        };
        db.VarFiles.Add(fixedVar);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        broken.SupersededByVarFileId = fixedVar.Id;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return fixedVar.Id;
    }
}
