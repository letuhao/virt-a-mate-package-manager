using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Domain.Indexing;
using VarVault.Domain.Safety;
using VarVault.Infrastructure.Indexing;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Edit meta.json for an online real <c>.var</c>: rewrite zip → trash original → swap into place → refresh catalog.
/// </summary>
public sealed class EfVarMetaEditService(
    VarVaultDbContext db,
    IVarInspector inspector,
    ITrashService trash,
    IDependencyResolver resolver,
    IWriteQueue writeQueue,
    IClock clock) : IVarMetaEditService
{
    public async Task<Result<VarMetaEditDraft>> LoadAsync(
        long packageId,
        long? varFileId = null,
        CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveEditableCopyAsync(packageId, varFileId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
            return Result.Failure<VarMetaEditDraft>(resolved.Error);

        var (vf, repo, absolutePath, warnings) = resolved.Value;
        string raw;
        try
        {
            await using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("meta.json")
                        ?? zip.Entries.FirstOrDefault(e =>
                            string.Equals(Path.GetFileName(e.FullName), "meta.json", StringComparison.OrdinalIgnoreCase)
                            && !e.FullName.Contains('/') && !e.FullName.Contains('\\'));
            if (entry is null)
                return Result.Failure<VarMetaEditDraft>("meta.missing", "meta.json is missing from the var.");
            await using var es = entry.Open();
            using var reader = new StreamReader(es, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            raw = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return Result.Failure<VarMetaEditDraft>("meta.io", ex.Message);
        }

        var parsed = VarMetaParser.Parse(raw);
        if (parsed.IsFailure)
            return Result.Failure<VarMetaEditDraft>(parsed.Error);

        var meta = parsed.Value;
        var package = await db.Packages.AsNoTracking()
            .FirstAsync(p => p.Id == packageId, cancellationToken)
            .ConfigureAwait(false);

        return new VarMetaEditDraft(
            packageId,
            vf.Id,
            package.VarName,
            absolutePath,
            meta.Creator ?? package.MetaCreator,
            meta.Package ?? package.MetaPackage,
            meta.LicenseType ?? package.LicenseType,
            meta.Description ?? package.Description,
            meta.ProgramVersion ?? package.ProgramVersion,
            meta.DependencyRefs,
            warnings);
    }

    public Task<Result<VarMetaEditResult>> SaveAsync(
        VarMetaEditRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(request);
        return writeQueue.EnqueueAsync(ct => SaveCoreAsync(request, ct), cancellationToken: cancellationToken);
    }

    private async Task<Result<VarMetaEditResult>> SaveCoreAsync(VarMetaEditRequest request, CancellationToken cancellationToken)
    {
        foreach (var raw in request.DependencyRefs)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                continue;
            var parsed = DependencyRef.Parse(trimmed);
            if (parsed.IsFailure)
                return Result.Failure<VarMetaEditResult>(parsed.Error);
        }

        var resolved = await ResolveEditableCopyAsync(request.PackageId, request.VarFileId, cancellationToken)
            .ConfigureAwait(false);
        if (resolved.IsFailure)
            return Result.Failure<VarMetaEditResult>(resolved.Error);

        var (vf, repo, absolutePath, _) = resolved.Value;

        string rawMeta;
        try
        {
            await using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("meta.json")
                        ?? zip.Entries.FirstOrDefault(e =>
                            string.Equals(Path.GetFileName(e.FullName), "meta.json", StringComparison.OrdinalIgnoreCase)
                            && !e.FullName.Contains('/') && !e.FullName.Contains('\\'));
            if (entry is null)
                return Result.Failure<VarMetaEditResult>("meta.missing", "meta.json is missing from the var.");
            await using var es = entry.Open();
            using var reader = new StreamReader(es, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            rawMeta = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return Result.Failure<VarMetaEditResult>("meta.io", ex.Message);
        }

        var applied = MetaJsonEditor.Apply(
            rawMeta,
            request.CreatorName,
            request.PackageName,
            request.LicenseType,
            request.Description,
            request.ProgramVersion,
            request.DependencyRefs);
        if (applied.IsFailure)
            return Result.Failure<VarMetaEditResult>(applied.Error);

        // Stage beside the source; rewriter uses "{dest}.partial" then renames to dest.
        var stagedPath = absolutePath + ".edited";
        var rewrite = await MetaZipRewriter.RewriteAsync(absolutePath, stagedPath, applied.Value, cancellationToken)
            .ConfigureAwait(false);
        if (rewrite.IsFailure)
            return Result.Failure<VarMetaEditResult>(rewrite.Error);

        // Destructive commit — do not observe cancellation past this point (would orphan trash/swap).
        var finishCt = CancellationToken.None;

        var trashed = await trash.TrashAsync(absolutePath, "meta-edit", finishCt).ConfigureAwait(false);
        if (trashed.IsFailure)
        {
            TryDelete(stagedPath);
            return Result.Failure<VarMetaEditResult>(trashed.Error);
        }

        try
        {
            File.Move(stagedPath, absolutePath);
        }
        catch (Exception ex)
        {
            TryDelete(stagedPath);
            await trash.RestoreAsync(trashed.Value.Id, finishCt).ConfigureAwait(false);
            return Result.Failure<VarMetaEditResult>("meta.swap", ex.Message);
        }

        // Disk is already swapped; catalog must update even if inspect is soft-degraded.
        var inspection = inspector.Inspect(absolutePath, finishCt);
        var insp = inspection.IsSuccess ? inspection.Value : null;
        var info = new FileInfo(absolutePath);
        var now = clock.UtcNow.UtcDateTime;

        var tracked = await db.VarFiles.FirstAsync(v => v.Id == vf.Id, finishCt).ConfigureAwait(false);
        tracked.SizeBytes = info.Exists ? info.Length : tracked.SizeBytes;
        tracked.FileMtime = info.Exists ? info.LastWriteTimeUtc : now;
        if (insp?.Signatures is { } sigs)
        {
            tracked.ContentSignature = sigs.ContentSignature;
            tracked.PayloadSignature = sigs.PayloadSignature;
            tracked.ContentSignatureNoPath = sigs.ContentSignatureNoPath;
        }
        tracked.IndexedAt = now;

        var package = await db.Packages.FirstAsync(p => p.Id == request.PackageId, finishCt).ConfigureAwait(false);
        package.MetaCreator = insp?.Meta?.Creator ?? request.CreatorName;
        package.MetaPackage = insp?.Meta?.Package ?? request.PackageName;
        package.LicenseType = insp?.Meta?.LicenseType ?? request.LicenseType;
        package.Description = insp?.Meta?.Description ?? request.Description;
        package.ProgramVersion = insp?.Meta?.ProgramVersion ?? request.ProgramVersion;
        package.LastIndexedAt = now;
        package.MetaDivergent = ComputeMetaDivergent(package, package.MetaCreator, package.MetaPackage);

        // Replace ALL deps (meta + embedded) like EfCatalogStore — Meta-only replace hits UNIQUE(VarFileId, DependsOnRefKey).
        var existingDeps = await db.Dependencies
            .Where(d => d.VarFileId == vf.Id)
            .ToListAsync(finishCt)
            .ConfigureAwait(false);
        if (existingDeps.Count > 0)
            db.Dependencies.RemoveRange(existingDeps);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddDepRows(vf.Id, insp?.Meta?.DependencyRefs ?? request.DependencyRefs, RefKind.Meta, seen);
        if (insp is not null)
            AddDepRows(vf.Id, insp.EmbeddedRefs, RefKind.Embedded, seen);

        var listItem = await db.PackageListItems.FirstOrDefaultAsync(i => i.PackageId == package.Id, finishCt)
            .ConfigureAwait(false);
        if (listItem is not null && package.CanonicalVarFileId == vf.Id)
            listItem.TotalSize = tracked.SizeBytes;

        await db.SaveChangesAsync(finishCt).ConfigureAwait(false);
        await resolver.ResolveAllAsync(finishCt).ConfigureAwait(false);

        var metaDepCount = (insp?.Meta?.DependencyRefs ?? request.DependencyRefs)
            .Count(r => !string.IsNullOrWhiteSpace(r));
        return new VarMetaEditResult(
            package.Id,
            vf.Id,
            absolutePath,
            trashed.Value.Id,
            metaDepCount);
    }

    private void AddDepRows(long varFileId, IReadOnlyList<string> refsRaw, RefKind kind, HashSet<string> seen)
    {
        foreach (var raw in refsRaw)
        {
            var key = IdentityFold.Compute(raw);
            if (key.Length == 0 || !seen.Add(key))
                continue;
            db.Dependencies.Add(new Dependency
            {
                VarFileId = varFileId,
                DependsOnRefKey = key,
                DependsOnRefRaw = raw,
                RefKind = kind,
                IsMissing = true,
            });
        }
    }

    private async Task<Result<(VarFile Vf, Repository Repo, string AbsolutePath, IReadOnlyList<string> Warnings)>> ResolveEditableCopyAsync(
        long packageId,
        long? varFileId,
        CancellationToken cancellationToken)
    {
        var package = await db.Packages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packageId, cancellationToken)
            .ConfigureAwait(false);
        if (package is null)
            return Result.Failure<(VarFile, Repository, string, IReadOnlyList<string>)>("meta.package", "Package not found.");

        var copies = await db.VarFiles.AsNoTracking()
            .Include(v => v.Repository)
            .Where(v => v.PackageId == packageId && v.SupersededByVarFileId == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        VarFile? chosen = null;
        if (varFileId is { } id)
            chosen = copies.FirstOrDefault(c => c.Id == id);
        else if (package.CanonicalVarFileId is { } canonical)
            chosen = copies.FirstOrDefault(c => c.Id == canonical);
        chosen ??= copies
            .Where(c => c.Repository is { IsOnline: true })
            .OrderBy(c => c.Repository!.Tier)
            .ThenByDescending(c => c.SizeBytes)
            .FirstOrDefault();
        chosen ??= copies.FirstOrDefault();

        if (chosen?.Repository is null)
            return Result.Failure<(VarFile, Repository, string, IReadOnlyList<string>)>("meta.copy", "No editable copy found.");

        if (!chosen.Repository.IsOnline)
            return Result.Failure<(VarFile, Repository, string, IReadOnlyList<string>)>("meta.offline", "Repository is offline.");

        var absolute = Path.Combine(chosen.Repository.MountPath, chosen.RelativePath);
        if (!LooseVarEnumerator.IsRealFile(absolute))
            return Result.Failure<(VarFile, Repository, string, IReadOnlyList<string>)>(
                "meta.symlink", "Refusing to edit a symlink / missing-link farm file. Pick a real repo copy.");

        if (chosen.EncodingHealth is EncodingHealth.NeedsFix or EncodingHealth.PartiallyBroken)
            return Result.Failure<(VarFile, Repository, string, IReadOnlyList<string>)>(
                "meta.encoding",
                "This var needs an encoding fix first — rewriting meta would risk corrupting entry names.");

        var warnings = new List<string>();
        var onlineCount = copies.Count(c => c.Repository is { IsOnline: true });
        if (onlineCount > 1)
            warnings.Add($"Only this copy will be edited ({onlineCount} online copies exist).");

        return (chosen, chosen.Repository, absolute, warnings);
    }

    private static bool ComputeMetaDivergent(Package package, string? metaCreator, string? metaPackage)
    {
        if (string.IsNullOrWhiteSpace(metaCreator) || string.IsNullOrWhiteSpace(metaPackage))
            return false;
        var fnFold = IdentityFold.Compute($"{package.Creator}.{package.PackageName}");
        var metaFold = IdentityFold.Compute($"{metaCreator}.{metaPackage}");
        return !string.Equals(fnFold, metaFold, StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
