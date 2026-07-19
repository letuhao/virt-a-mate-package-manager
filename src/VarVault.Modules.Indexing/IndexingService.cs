using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.Domain.ValueObjects;
using VarVault.Sdk.Indexing;

namespace VarVault.Modules.Indexing;

/// <summary>
/// The staged, incremental indexing orchestrator. Enumerates a repository, skips unchanged vars by
/// (size,mtime), inspects new/changed ones (identity + meta + signatures + classification + encoding),
/// upserts catalog rows through <see cref="ICatalogStore"/>, prunes vanished files (online repos only),
/// and refreshes the read model. Binds to no EF/Infrastructure type — only Domain/SDK seams. (IDX-1/2.)
/// </summary>
internal sealed class IndexingService(
    IServiceScopeFactory scopeFactory,
    IRepositoryEnumerator enumerator,
    IVarInspector inspector,
    ILogger<IndexingService> logger) : IIndexingService
{
    public async Task<IndexResult> IndexRepositoryAsync(
        Guid repositoryId,
        string repositoryMountPath,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.StartActivity("index.repository");
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICatalogStore>();
        var previews = scope.ServiceProvider.GetRequiredService<IPreviewIndexer>();

        var online = await store.RepositoryIsOnlineAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        var existing = (await store.ListVarFilesAsync(repositoryId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(v => v.RelativePath, StringComparer.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirty = new ReadModelDirtySet(); // base-table writes mark packages; drained into one refresh (0.26)
        int indexed = 0, skipped = 0, corrupt = 0, unrecognized = 0;

        foreach (var scanned in enumerator.Enumerate(repositoryMountPath, includeQuarantined: true, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(scanned.RelativePath);

            if (existing.TryGetValue(scanned.RelativePath, out var ex) &&
                RepositoryScanRules.IsFresh(ex.SizeBytes, ex.FileMtimeUtc, scanned.SizeBytes, scanned.FileMtimeUtc))
            {
                skipped++;
                continue; // fresh — no file open (IDX-2)
            }

            var inspection = inspector.Inspect(scanned.FullPath, cancellationToken);
            if (inspection.IsFailure)
            {
                logger.LogWarning("Skipping unreadable var {Path}: {Error}", scanned.RelativePath, inspection.Error);
                continue;
            }

            var upsert = BuildUpsert(repositoryId, scanned, inspection.Value, out var wasUnrecognized, out var wasCorrupt);
            if (wasUnrecognized) unrecognized++;
            if (wasCorrupt) corrupt++;

            var packageId = await store.ApplyAsync(upsert, cancellationToken).ConfigureAwait(false);
            dirty.MarkPackage(packageId);
            indexed++;
        }

        // Prune vanished files — only when the repository is confirmed online (offline ≠ gone). (1.28 ⚠)
        var pruned = 0;
        if (online)
        {
            var vanished = existing.Where(kv => !seen.Contains(kv.Key)).Select(kv => kv.Value).ToList();
            if (vanished.Count > 0)
            {
                foreach (var v in vanished)
                    dirty.MarkPackage(v.PackageId);
                pruned = await store.RemoveVarFilesAsync(vanished.Select(v => v.Id).ToList(), cancellationToken).ConfigureAwait(false);
            }
        }

        // Pass-1 complete: refresh the read model so the catalog is browsable (names/meta/deps/counts). (1.23)
        var affected = dirty.Drain();
        if (affected.Count > 0)
            await store.RefreshReadModelAsync(affected, cancellationToken).ConfigureAwait(false);

        // Pass-2: previews/thumbnails fill in afterwards — the gallery already shows type placeholders. (1.23/1.32)
        if (affected.Count > 0)
            await previews.BuildPreviewsAsync(affected, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Indexed repository {RepositoryId}: {Indexed} indexed, {Skipped} skipped, {Pruned} pruned, {Corrupt} corrupt, {Unrecognized} unrecognized",
            repositoryId, indexed, skipped, pruned, corrupt, unrecognized);

        return new IndexResult(indexed, skipped, pruned, corrupt, unrecognized);
    }

    private static VarUpsert BuildUpsert(
        Guid repositoryId,
        ScannedVar scanned,
        VarInspection inspection,
        out bool unrecognized,
        out bool corrupt)
    {
        var fileName = System.IO.Path.GetFileName(scanned.RelativePath);
        var parse = PackageId.TryParse(fileName);
        var identity = parse.IsSuccess ? parse.Value : null;
        unrecognized = identity is null;
        corrupt = inspection.Integrity == IntegrityStatus.CorruptZip;

        // BadName overrides only when the zip itself is otherwise fine.
        var integrity = inspection.Integrity;
        if (identity is null && integrity == IntegrityStatus.Ok)
            integrity = IntegrityStatus.BadName;

        var meta = inspection.Meta;
        var encoding = inspection.Encoding;
        var signatures = inspection.Signatures;

        var gender = GenderInference.Infer(inspection.Entries.Select(e => e.DecodedNameBestEffort));
        var items = inspection.Classification?.Items ?? [];
        var contentItems = items
            .Select(i => new UpsertContentItem(i.Type, i.EntryPath, i.IsPreset,
                gender.Gender == Gender.Auto ? null : gender.Gender,
                gender.Gender == Gender.Auto ? null : gender.Confidence))
            .ToList();

        var counts = inspection.Classification?.Counts ?? new Dictionary<ContentType, int>();

        return new VarUpsert(
            RepositoryId: repositoryId,
            RelativePath: scanned.RelativePath,
            SizeBytes: scanned.SizeBytes,
            FileMtimeUtc: scanned.FileMtimeUtc,
            Quarantine: scanned.Quarantine,
            Identity: identity,
            Integrity: integrity,
            MetaCreator: meta?.Creator,
            MetaPackage: meta?.Package,
            LicenseType: meta?.LicenseType,
            Description: meta?.Description,
            ProgramVersion: meta?.ProgramVersion,
            ContentSignature: signatures?.ContentSignature,
            PayloadSignature: signatures?.PayloadSignature,
            ContentSignatureNoPath: signatures?.ContentSignatureNoPath,
            EncodingHealth: encoding?.Health ?? EncodingHealth.Unknown,
            DetectedCodepage: encoding?.DetectedCodepage,
            BrokenEntryCount: encoding?.BrokenEntryCount ?? 0,
            ContentItems: contentItems,
            ContentCounts: counts,
            DependencyRefsRaw: meta?.DependencyRefs ?? [],
            EmbeddedRefsRaw: inspection.EmbeddedRefs);
    }
}
