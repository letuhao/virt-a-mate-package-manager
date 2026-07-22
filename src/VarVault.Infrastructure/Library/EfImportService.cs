using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Common.Diagnostics;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Domain.Fingerprinting;
using VarVault.Domain.Import;
using VarVault.Domain.Indexing;
using VarVault.Domain.Migration;
using VarVault.Domain.ValueObjects;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Events;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexer;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Presets;
using VarVault.Sdk.Settings;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Import service (doc 30). <see cref="ScanAsync"/> extracts archives + classifies each incoming var against the
/// <b>whole library</b> (D1) via the domain <see cref="ImportClassifier"/>, recommends a resolution for conflicts
/// (<see cref="ConflictRecommender"/>), and builds the per-item diff + gallery preview. <see cref="ApplyAsync"/>
/// durably copies/fixes/renames approved vars, indexes them, records history, optionally activates the imported set
/// into VaM (D2), and publishes <see cref="VarsImported"/>. (doc 30/31.)
/// </summary>
public sealed class EfImportService(
    VarVaultDbContext db, IVarInspector inspector, IArchiveExtractor extractor, ISettingsService settings,
    IDurableFileMover mover, IEncodingFixer fixer, IIndexerClient indexer, IImportHistoryStore history,
    IPresetService presets, IActivationService activation, IEventBus events)
    : IImportService
{
    private const string TempDirKey = "import.temp_dir";
    private const string ImportedPresetName = "Imported";

    /// <summary>One catalogued var reduced to what dedup + the "existing" ref need. Loaded once per scan.</summary>
    private sealed record CatalogFact(
        long VarFileId, string IdentityKey, string? ContentSignature, int Tier, string RepositoryName,
        Guid RepositoryId, string AbsolutePath, bool IsOnline);

    public async Task<ImportSession> ScanAsync(ImportSpec spec, IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(spec);
        var sessionId = Guid.NewGuid();

        // Temp workspace on the target repo's drive (or a setting override) — archives extract here. (§6/D4)
        var targetMount = await db.Repositories.Where(r => r.Id == spec.TargetRepositoryId)
            .Select(r => r.MountPath).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var tempSetting = await settings.GetAsync(TempDirKey, cancellationToken).ConfigureAwait(false);
        var workspace = Import.TempWorkspace.ForSession(tempSetting, targetMount, sessionId);

        try
        {
            return await ScanCoreAsync(spec, workspace, progress, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await workspace.DisposeAsync().ConfigureAwait(false); // clean temp on cancel or any failure (§11)
            throw;
        }
    }

    private async Task<ImportSession> ScanCoreAsync(ImportSpec spec, ITempWorkspace workspace,
        IProgressSink? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new ProgressReport(0, 1, "Preparing sources…"));

        // ── Resolve sources: folders (loose vars + nested archives) and top-level archives. (§6) ────────
        var sources = new List<ImportSource>();
        var varRefs = new List<(string Label, string SourcePath, string VarPath)>();
        var paths = spec.Paths ?? [];
        var pathIndex = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ProgressReport(pathIndex, Math.Max(1, paths.Count),
                $"Preparing {Path.GetFileName(path)}…"));
            if (Directory.Exists(path))
            {
                var label = new DirectoryInfo(path).Name;
                var loose = LooseVarEnumerator.EnumerateVarFiles(path, cancellationToken).ToList();
                foreach (var f in loose)
                    varRefs.Add((label, path, f));
                sources.Add(new ImportSource(path, ImportSourceKind.Folder, ImportSourceStatus.Ok, loose.Count, null));

                foreach (var archive in LooseVarEnumerator.EnumerateArchiveFiles(path, cancellationToken))
                {
                    progress?.Report(new ProgressReport(pathIndex, Math.Max(1, paths.Count),
                        $"Extracting {Path.GetFileName(archive)}…"));
                    sources.Add(await ExtractArchiveAsync(archive, workspace, varRefs, cancellationToken).ConfigureAwait(false));
                }
            }
            else if (File.Exists(path) && IsArchive(path))
            {
                progress?.Report(new ProgressReport(pathIndex, Math.Max(1, paths.Count),
                    $"Extracting {Path.GetFileName(path)}…"));
                sources.Add(await ExtractArchiveAsync(path, workspace, varRefs, cancellationToken).ConfigureAwait(false));
            }

            pathIndex++;
        }

        progress?.Report(new ProgressReport(0, 1, "Loading catalog…"));

        // ── Load the whole library's dedup facts once (D1: dedup across all repos). ─────────────────────
        var catalog = (await db.VarFiles
            .Where(v => v.Package != null && v.Repository != null)
            .Select(v => new
            {
                v.Id,
                v.Package!.IdentityKey,
                v.ContentSignature,
                v.Repository!.Tier,
                RepoName = v.Repository.Name,
                RepoId = v.Repository.Id,
                v.Repository.MountPath,
                v.RelativePath,
                v.Repository.IsOnline,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(v => new CatalogFact(v.Id, v.IdentityKey, v.ContentSignature, v.Tier, v.RepoName,
                v.RepoId, Path.Combine(v.MountPath, v.RelativePath), v.IsOnline))
            .ToList();

        var catalogFacts = catalog
            .Select(c => new CatalogVarFacts(c.IdentityKey, c.ContentSignature))
            .ToList();

        // Gallery previews (6.3) are extracted per-file into the session temp — cleaned with the workspace.
        var thumbsDir = workspace.NewDir("thumbs");

        progress?.Report(new ProgressReport(0, Math.Max(1, varRefs.Count), "Classifying…"));

        // ── Classify each incoming var (with intra-batch dedup, 4.2). ───────────────────────────────────
        var items = new List<ImportItem>(varRefs.Count);
        var batchSigs = new HashSet<string>(StringComparer.Ordinal);
        var done = 0;
        foreach (var (label, spath, vpath) in varRefs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(BuildItem(label, spath, vpath, thumbsDir, catalog, catalogFacts, batchSigs, cancellationToken));
            progress?.Report(new ProgressReport(++done, Math.Max(1, varRefs.Count),
                $"Classifying {Path.GetFileName(vpath)} ({done}/{varRefs.Count})"));
        }

        progress?.Report(new ProgressReport(Math.Max(1, varRefs.Count), Math.Max(1, varRefs.Count), "Scan complete"));

        // §9 telemetry: how many vars were scanned + the extracted temp footprint on disk.
        Telemetry.ImportScanned.Add(items.Count);
        Telemetry.ImportTempBytes.Record(DirectorySize(workspace.Root));

        var warnings = await BuildDedupTrustWarningsAsync(spec.TargetRepositoryId, catalog, cancellationToken).ConfigureAwait(false);

        return new ImportSession(SessionIdFrom(workspace), workspace.Root, spec.TargetRepositoryId,
            spec.ActivateAfter, sources, items, warnings);
    }

    /// <summary>
    /// D1 dedup-trust warnings (§3/D1): whole-library dedup is only reliable if the catalog is complete. Surface —
    /// don't silently trust — the two cheap staleness signals: (a) any <b>offline</b> repo (its current on-disk state
    /// can't be confirmed, so a match there may be missed) and (b) the <b>target</b> repo looking <b>unindexed</b>
    /// (has <c>.var</c> files on disk but no catalog rows → an existing copy could slip through as New and get
    /// duplicated). The target check is bounded to one repo for perf (2M-scale); it never enumerates every repo.
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildDedupTrustWarningsAsync(
        Guid targetRepoId, IReadOnlyList<CatalogFact> catalog, CancellationToken ct)
    {
        var repos = await db.Repositories
            .Select(r => new { r.Id, r.Name, r.MountPath, r.IsOnline })
            .ToListAsync(ct).ConfigureAwait(false);

        var warnings = new List<string>();
        foreach (var r in repos.Where(r => !r.IsOnline))
            warnings.Add($"Repo '{r.Name}' is offline — the duplicate check may miss copies already there.");

        var target = repos.FirstOrDefault(r => r.Id == targetRepoId);
        if (target is not null
            && catalog.All(c => c.RepositoryId != targetRepoId)
            && TargetHasVarsOnDisk(target.MountPath))
        {
            warnings.Add($"Target repo '{target.Name}' isn't indexed yet — existing copies may be treated as 'New' and duplicated. Index the repo before importing.");
        }

        return warnings;
    }

    private static bool TargetHasVarsOnDisk(string? mount)
    {
        try
        {
            return !string.IsNullOrEmpty(mount) && Directory.Exists(mount)
                && Directory.EnumerateFiles(mount, "*.var", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    private static long DirectorySize(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : 0;
        }
        catch { return 0; }
    }

    private static Guid SessionIdFrom(ITempWorkspace workspace) =>
        Guid.TryParseExact(new DirectoryInfo(workspace.Root).Name, "N", out var id) ? id : Guid.NewGuid();

    /// <summary>Extract one archive into the workspace; on success queue its vars, on failure return a failed source. (4.3)</summary>
    private async Task<ImportSource> ExtractArchiveAsync(
        string archivePath, ITempWorkspace workspace,
        List<(string, string, string)> varRefs, CancellationToken ct)
    {
        var name = Path.GetFileName(archivePath);
        var dest = workspace.NewDir(name + "_" + Guid.NewGuid().ToString("N")[..8]);
        var result = await extractor.ExtractAsync(archivePath, dest, ct).ConfigureAwait(false);
        if (!result.Success)
            return new ImportSource(archivePath, ImportSourceKind.Archive, result.Status, 0, result.FailReason);

        foreach (var f in Directory.EnumerateFiles(dest, "*.var", SearchOption.AllDirectories))
            varRefs.Add((name, archivePath, f));
        return new ImportSource(archivePath, ImportSourceKind.Archive, ImportSourceStatus.Ok, result.ExtractedCount, null);
    }

    private ImportItem BuildItem(
        string label, string sourcePath, string varPath, string thumbsDir,
        IReadOnlyList<CatalogFact> catalog, IReadOnlyList<CatalogVarFacts> catalogFacts,
        HashSet<string> batchSigs, CancellationToken ct)
    {
        var fileName = Path.GetFileName(varPath);
        var inspection = inspector.Inspect(varPath, ct);

        // I/O failure (locked/missing) → treat as a corrupt/unreadable item so the run still lists it.
        if (inspection.IsFailure)
        {
            var badSignals = UnknownSignals(varPath, fileName);
            return Item(fileName, sourcePath, label, badSignals, ImportLane.Corrupt, ImportDecision.Discard,
                "Couldn't read the file (locked / missing).", null, []);
        }

        var insp = inspection.Value;
        var parsed = PackageId.TryParse(fileName);
        var filenameIdentityKey = parsed.IsSuccess ? parsed.Value.IdentityKey : null;
        var signals = BuildSignals(varPath, fileName, insp, parsed, thumbsDir);
        var contentSig = insp.Signatures?.ContentSignature;

        // 4.2 · intra-batch dedup: the same content twice in this import → keep the first, skip the rest.
        if (!string.IsNullOrEmpty(contentSig) && !batchSigs.Add(contentSig) && insp.Integrity == IntegrityStatus.Ok)
            return Item(fileName, sourcePath, label, signals, ImportLane.Exact, ImportDecision.Skip,
                "Duplicate of another file in this same import — will be skipped.", null, []);

        // E1: fold(MetaCreator.MetaPackage.<filenameVersion>) — meta.json has no version, so borrow the filename's.
        // Only when the filename parses (we need a version to form a full identity key to match the catalog).
        string? metaIdentityKey = signals.MetaIdentity is { } metaCp && parsed.IsSuccess
            ? IdentityFold.Compute($"{metaCp}.{parsed.Value.VersionToken}")
            : null;

        var candidate = new ImportCandidateFacts(
            insp.Integrity,
            contentSig,
            filenameIdentityKey,
            signals.MetaDivergent,
            HasEncodingIssue(insp),
            metaIdentityKey);

        var lane = Map(ImportClassifier.Classify(candidate, catalogFacts));

        // For a Conflict, resolve against whichever key actually matched the catalog: the filename identity if it
        // collides, else the E1 meta identity (a misnamed var whose meta resolves to an existing var).
        var conflictKey = filenameIdentityKey is not null && catalog.Any(c => c.IdentityKey == filenameIdentityKey)
            ? filenameIdentityKey
            : metaIdentityKey ?? filenameIdentityKey;

        return lane switch
        {
            ImportLane.New => Item(fileName, sourcePath, label, signals, lane, ImportDecision.Import,
                "New file — will be imported into the repo.", null, [], incomingPath: varPath),

            ImportLane.Exact => ExactItem(fileName, sourcePath, label, signals, insp, catalog, varPath),

            ImportLane.Cjk => Item(fileName, sourcePath, label, signals, lane, ImportDecision.ImportAndFix,
                $"Valid var with {signals.GbkEntryCount} GBK-encoded entry name(s) — will import and auto-fix to Unicode.", null, [], incomingPath: varPath),

            ImportLane.Conflict => ConflictItem(fileName, sourcePath, label, signals, insp, conflictKey!, catalog, varPath, ct),

            ImportLane.Naming => Item(fileName, sourcePath, label, signals, lane, ImportDecision.RenameToMeta,
                signals.MetaIdentity is { } m ? $"Filename doesn't match meta.json (meta: {m})." : "Filename couldn't be parsed.",
                null, [], decision: ImportDecision.None, incomingPath: varPath),

            _ /* Corrupt */ => Item(fileName, sourcePath, label, signals, ImportLane.Corrupt, ImportDecision.Discard,
                $"Corrupt file ({signals.IntegrityStatus}) — recommend discarding.", null, [], decision: ImportDecision.None, incomingPath: varPath),
        };
    }

    private ImportItem ExactItem(string fileName, string sourcePath, string label, ImportSignals signals,
        VarInspection insp, IReadOnlyList<CatalogFact> catalog, string varPath)
    {
        var match = catalog.FirstOrDefault(c => Sig(c.ContentSignature) == Sig(insp.Signatures?.ContentSignature));
        var reason = match is null
            ? "Already in the library — will be skipped."
            : $"Already in repo '{match.RepositoryName}' (T{match.Tier}) — will be skipped.";
        var existing = match is null ? null
            : new ExistingRef(match.VarFileId, match.Tier, match.RepositoryName, match.AbsolutePath, signals);
        return Item(fileName, sourcePath, label, signals, ImportLane.Exact, ImportDecision.Skip, reason, existing, [], incomingPath: varPath);
    }

    private ImportItem ConflictItem(string fileName, string sourcePath, string label, ImportSignals incomingSignals,
        VarInspection incoming, string identityKey, IReadOnlyList<CatalogFact> catalog, string varPath, CancellationToken ct)
    {
        // Prefer an online copy of the same identity to compare against.
        var match = catalog.Where(c => c.IdentityKey == identityKey).OrderByDescending(c => c.IsOnline).FirstOrDefault();
        ImportSignals? existingSignals = null;
        VarInspection? existingInsp = null;
        if (match is not null && File.Exists(match.AbsolutePath))
        {
            var r = inspector.Inspect(match.AbsolutePath, ct);
            if (r.IsSuccess)
            {
                existingInsp = r.Value;
                existingSignals = BuildSignals(match.AbsolutePath, Path.GetFileName(match.AbsolutePath), r.Value,
                    PackageId.TryParse(Path.GetFileName(match.AbsolutePath)));
            }
        }

        var rec = ConflictRecommender.Recommend(
            ToSide(incomingSignals, incoming),
            existingSignals is not null && existingInsp is not null
                ? ToSide(existingSignals, existingInsp)
                : new ConflictSide(false, false, 0, 0, 0, DateTime.MinValue)); // no readable existing → treat as broken

        var decision = MapChoice(rec.Choice);
        var existingRef = match is null ? null
            : new ExistingRef(match.VarFileId, match.Tier, match.RepositoryName, match.AbsolutePath,
                existingSignals ?? UnknownSignals(match.AbsolutePath, Path.GetFileName(match.AbsolutePath)));
        var diff = existingInsp is null ? [] : BuildDiff(incoming, existingInsp);

        return Item(fileName, sourcePath, label, incomingSignals, ImportLane.Conflict, decision, rec.Reason,
            existingRef, diff, decision: ImportDecision.None, incomingPath: varPath);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────
    private static ImportItem Item(string fileName, string sourcePath, string label, ImportSignals signals,
        ImportLane lane, ImportDecision recommendation, string reason, ExistingRef? existing,
        IReadOnlyList<EntryDelta> diff, ImportDecision? decision = null, string incomingPath = "")
    {
        // Auto lanes are pre-decided (their recommendation); review lanes start at None.
        var d = decision ?? (lane is ImportLane.Conflict or ImportLane.Naming or ImportLane.Corrupt
            ? ImportDecision.None : recommendation);
        return new ImportItem(Guid.NewGuid(), label, sourcePath, fileName, incomingPath, signals.FilenameIdentity, lane,
            signals, recommendation, reason, existing, diff) { Decision = d };
    }

    private static ImportSignals BuildSignals(string path, string fileName, VarInspection insp, Common.Result<PackageId> parsed,
        string? thumbsDir = null)
    {
        long size = 0; DateTime mtime = default;
        try { var fi = new FileInfo(path); size = fi.Length; mtime = fi.LastWriteTimeUtc; } catch { /* best-effort */ }

        // Gallery preview (6.3): incoming vars aren't catalogued, so pull a representative image out of the
        // archive into the session temp. Best-effort — a var with no image just shows a placeholder.
        var thumbPath = thumbsDir is null ? null : TryExtractPreview(path, insp, thumbsDir);

        var filenameIdentity = parsed.IsSuccess ? parsed.Value.VarName : fileName;
        string? metaIdentity = insp.Meta is { Creator: { } mc, Package: { } mp } && !string.IsNullOrWhiteSpace(mc)
            ? $"{mc}.{mp}" : null;

        var metaDivergent = false;
        if (parsed.IsSuccess && metaIdentity is not null)
        {
            var fnFold = IdentityFold.Compute($"{parsed.Value.Creator}.{parsed.Value.Package}");
            var metaFold = IdentityFold.Compute(metaIdentity);
            metaDivergent = !string.Equals(fnFold, metaFold, StringComparison.Ordinal);
        }
        else if (!parsed.IsSuccess && metaIdentity is not null)
        {
            metaDivergent = true; // unparseable filename but a real meta identity → treat name as untrustworthy
        }

        return new ImportSignals(
            ValidZip: insp.Integrity != IntegrityStatus.CorruptZip,
            IntegrityStatus: insp.Integrity.ToString(),
            SizeBytes: size,
            EntryCount: insp.Entries.Count,
            FileMtime: mtime,
            HasPreview: thumbPath is not null,
            PreviewThumbPath: thumbPath,
            FilenameIdentity: filenameIdentity,
            MetaIdentity: metaIdentity,
            MetaDivergent: metaDivergent,
            Codepage: insp.Encoding?.DetectedCodepage,
            GbkEntryCount: EncodingHealthEngine.CountLegacyEntries(insp.Entries));
    }

    /// <summary>
    /// Pull a representative preview image out of an incoming var into <paramref name="thumbsDir"/> and return its
    /// path (or null when the var has no usable image). Prefers a scene/look sibling <c>.jpg</c>, else the largest
    /// image entry. Best-effort: any zip error yields a placeholder. (6.3.)
    /// </summary>
    private static string? TryExtractPreview(string varPath, VarInspection insp, string thumbsDir)
    {
        try
        {
            // Candidate images by decoded name; skip directories + tiny icons.
            var images = insp.Entries
                .Where(e => !e.IsDirectory && IsImageName(e.DecodedNameBestEffort))
                .ToList();
            if (images.Count == 0)
                return null;

            // A jpg that sits beside a scene/look/preset json is almost always the intended preview;
            // otherwise fall back to the biggest image in the archive.
            var jsonBases = insp.Entries
                .Where(e => !e.IsDirectory && HasContentExtension(e.DecodedNameBestEffort))
                .Select(e => StripExtension(e.DecodedNameBestEffort))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var chosen = images.FirstOrDefault(e => jsonBases.Contains(StripExtension(e.DecodedNameBestEffort)))
                         ?? images.OrderByDescending(e => e.UncompressedSize).First();

            using var archive = ZipFile.OpenRead(varPath);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, chosen.DecodedNameBestEffort, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return null;

            var dest = Path.Combine(thumbsDir, Guid.NewGuid().ToString("N") + Path.GetExtension(entry.FullName));
            entry.ExtractToFile(dest, overwrite: true);
            return dest;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            return null; // placeholder in the gallery
        }
    }

    private static bool IsImageName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png";
    }

    private static bool HasContentExtension(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".json" or ".vap" or ".vam" or ".vaj";
    }

    private static string StripExtension(string name) => name[..^Path.GetExtension(name).Length];

    private static ImportSignals UnknownSignals(string path, string fileName) => new(
        false, IntegrityStatus.CorruptZip.ToString(), 0, 0, default, false, null, fileName, null, false, null, 0);

    private static ConflictSide ToSide(ImportSignals s, VarInspection insp) => new(
        Valid: insp.Integrity == IntegrityStatus.Ok,
        MetaDivergent: s.MetaDivergent,
        GbkEntryCount: s.GbkEntryCount,
        EntryCount: s.EntryCount,
        SizeBytes: s.SizeBytes,
        FileMtime: s.FileMtime);

    private static bool HasEncodingIssue(VarInspection insp) =>
        EncodingHealthEngine.CountLegacyEntries(insp.Entries) > 0;

    /// <summary>Per-entry diff between two vars by raw name + (size,CRC): + added, − removed, ~ changed. (Capped.)</summary>
    private static IReadOnlyList<EntryDelta> BuildDiff(VarInspection incoming, VarInspection existing)
    {
        const int cap = 200;
        static string Key(ZipEntryFacts e) => Convert.ToHexString(e.RawNameBytes);
        var inc = incoming.Entries.Where(e => !e.IsDirectory).ToDictionary(Key, e => e);
        var exi = existing.Entries.Where(e => !e.IsDirectory).ToDictionary(Key, e => e);
        var deltas = new List<EntryDelta>();
        foreach (var (k, e) in inc)
        {
            if (!exi.TryGetValue(k, out var ex)) deltas.Add(new EntryDelta('+', e.DecodedNameBestEffort));
            else if (ex.Crc32 != e.Crc32 || ex.UncompressedSize != e.UncompressedSize)
                deltas.Add(new EntryDelta('~', e.DecodedNameBestEffort));
            if (deltas.Count >= cap) return deltas;
        }
        foreach (var (k, e) in exi)
        {
            if (!inc.ContainsKey(k)) deltas.Add(new EntryDelta('-', e.DecodedNameBestEffort));
            if (deltas.Count >= cap) break;
        }
        return deltas;
    }

    private static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".zip" or ".7z" or ".rar" or ".tar" or ".gz";
    }

    private static string Sig(string? s) => s ?? "";

    private static ImportLane Map(LaneKind k) => k switch
    {
        LaneKind.New => ImportLane.New,
        LaneKind.Exact => ImportLane.Exact,
        LaneKind.Cjk => ImportLane.Cjk,
        LaneKind.Conflict => ImportLane.Conflict,
        LaneKind.Naming => ImportLane.Naming,
        _ => ImportLane.Corrupt,
    };

    private static ImportDecision MapChoice(KeepChoice c) => c switch
    {
        KeepChoice.KeepIncoming => ImportDecision.KeepIncoming,
        KeepChoice.KeepExisting => ImportDecision.KeepExisting,
        _ => ImportDecision.KeepBoth,
    };

    // ── Apply (doc 30 §7; doc 31 Phase 5). Durable copy + fix + rename into the target repo, then index,
    //    record history, and clean the temp workspace. ────────────────────────────────────────────────────
    public async Task<ApplyResult> ApplyAsync(ImportSession session, IProgressSink? progress = null, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(session);

        progress?.Report(new ProgressReport(0, 1, "Preflight…"));

        // §11 · block Apply with a clear message when the target repo is offline/missing or too full to hold the
        //       import — never start copying into a repo that can't take it. (reuses the online/capacity signals.)
        //       Uses None so a pre-cancelled token doesn't abort here — a graceful cancel is handled in the loop (E5).
        var target = await db.Repositories.Where(r => r.Id == session.TargetRepositoryId)
            .Select(r => new { r.MountPath, r.IsOnline }).FirstOrDefaultAsync(CancellationToken.None).ConfigureAwait(false);
        if (target is null || string.IsNullOrEmpty(target.MountPath) || !target.IsOnline || !Directory.Exists(target.MountPath))
            throw new InvalidOperationException("Target repo isn't available (offline or not found) — can't import.");
        var mount = target.MountPath;
        var plannedBytes = session.Items.Where(i => IsCopyDecision(i.Decision))
            .Sum(i => Math.Max(0, i.Signals.SizeBytes));
        if (!HasFreeSpace(mount, plannedBytes))
            throw new InvalidOperationException("Target repo doesn't have enough free space for this import — free some space or choose another repo.");

        var sw = Stopwatch.StartNew();
        using var activity = Telemetry.StartActivity("import.apply");
        int copied = 0, fixedCount = 0, renamed = 0, skipped = 0, discarded = 0, failed = 0;
        var didImport = false;
        var cancelled = false;   // E5: a graceful cancel mid-apply still records a partial run (remainder skipped).
        var importedRefs = new List<string>();   // identities of vars that landed, for activate-after (5.9)
        var outcomes = new List<ImportOutcome>(session.Items.Count);   // per-item results, persisted (§8 · G3)
        var copiedIncoming = new List<string>();
        var total = Math.Max(1, session.Items.Count);
        try
        {
          try
          {
            var done = 0;
            foreach (var item in session.Items)
            {
                if (cancellationToken.IsCancellationRequested) { cancelled = true; break; }
                bool ok = true;
                string? reason = null;
                switch (item.Decision)
                {
                    case ImportDecision.Import:
                    case ImportDecision.KeepIncoming:
                    {
                        var r = await CopyItemAsync(item, Path.Combine(mount, item.FileName), item.Signals.GbkEntryCount > 0, cancellationToken).ConfigureAwait(false);
                        if (r.Ok) { copied++; if (r.Fixed) fixedCount++; didImport = true; TrackImported(importedRefs, item, item.FileName); TrackCopiedIncoming(copiedIncoming, session, item); }
                        else { failed++; ok = false; reason = "copy failed"; }
                        break;
                    }
                    case ImportDecision.ImportAndFix:
                    {
                        var r = await CopyItemAsync(item, Path.Combine(mount, item.FileName), fix: true, cancellationToken).ConfigureAwait(false);
                        if (r.Ok) { copied++; if (r.Fixed) fixedCount++; didImport = true; TrackImported(importedRefs, item, item.FileName); TrackCopiedIncoming(copiedIncoming, session, item); }
                        else { failed++; ok = false; reason = "copy failed"; }
                        break;
                    }
                    case ImportDecision.RenameToMeta:
                    {
                        var targetName = RenameTarget(item);
                        var r = await CopyItemAsync(item, Uniquify(Path.Combine(mount, targetName)), item.Signals.GbkEntryCount > 0, cancellationToken).ConfigureAwait(false);
                        if (r.Ok) { renamed++; if (r.Fixed) fixedCount++; didImport = true; TrackImported(importedRefs, item, targetName); reason = "→ " + targetName; TrackCopiedIncoming(copiedIncoming, session, item); }
                        else { failed++; ok = false; reason = "copy failed"; }
                        break;
                    }
                    case ImportDecision.KeepBoth:
                    {
                        var r = await CopyItemAsync(item, Uniquify(Path.Combine(mount, item.FileName)), item.Signals.GbkEntryCount > 0, cancellationToken).ConfigureAwait(false);
                        if (r.Ok) { copied++; if (r.Fixed) fixedCount++; didImport = true; TrackImported(importedRefs, item, item.FileName); TrackCopiedIncoming(copiedIncoming, session, item); }
                        else { failed++; ok = false; reason = "copy failed"; }
                        break;
                    }
                    case ImportDecision.Discard: discarded++; reason = "discarded"; break;
                    default: skipped++; reason = "skipped"; break; // Skip / KeepExisting / None
                }
                outcomes.Add(new ImportOutcome(item.FileName, item.IdentityKey, item.Lane, item.Decision, ok, reason));
                done++;
                progress?.Report(new ProgressReport(done, total,
                    $"Applying {item.FileName} ({done}/{session.Items.Count})"));
            }
          }
          catch (OperationCanceledException)
          {
              // Cancelled mid-copy (mover/fixer observed the token). Fall through to record the partial run. (E5)
              cancelled = true;
          }

            // E5: on a graceful cancel, the un-processed remainder is recorded as skipped so history reflects reality.
            if (cancelled)
                foreach (var item in session.Items.Skip(outcomes.Count))
                {
                    skipped++;
                    outcomes.Add(new ImportOutcome(item.FileName, item.IdentityKey, item.Lane, item.Decision, true, "cancelled"));
                }

            // Index + activate + event only on a complete run; a cancelled run leaves its landed vars for the next
            // index pass (durable copies are already on disk) and doesn't announce a (partial) import.
            var activated = false;
            if (!cancelled)
            {
                if (didImport)
                {
                    progress?.Report(new ProgressReport(0, 1, "Indexing target repository…"));
                    await WaitForIndexAsync(session.TargetRepositoryId, cancellationToken, progress).ConfigureAwait(false);
                }

                // Optional activate-after (D2/5.9): link the just-imported vars into VaM via the existing preset flow.
                if (session.ActivateAfter && importedRefs.Count > 0)
                {
                    progress?.Report(new ProgressReport(0, 1, "Activating imported vars…"));
                    activated = await ActivateImportedAsync(importedRefs, cancellationToken).ConfigureAwait(false);
                }
            }

            var failedSources = session.Sources.Where(s => s.Status != ImportSourceStatus.Ok)
                .Select(s => new ImportFailedSource(s.Path, s.Kind, s.FailReason ?? s.Status.ToString())).ToList();
            var run = new ImportRun(Guid.NewGuid(), DateTime.UtcNow, session.TargetRepositoryId,
                SummarizeSources(session.Sources), copied, fixedCount, renamed, skipped, discarded, failed, failedSources, outcomes);
            // History is written even for a cancelled run (§11) — and always before temp cleanup in `finally`.
            progress?.Report(new ProgressReport(0, 1, "Recording history…"));
            var runId = await history.RecordAsync(run, CancellationToken.None).ConfigureAwait(false);

            // Metrics + event (5.7/§9) — after the run is persisted so subscribers see a consistent state.
            Telemetry.ImportsApplied.Add(1);
            Telemetry.ImportVarsCopied.Add(copied + renamed);
            Telemetry.ImportFixed.Add(fixedCount);
            if (failed > 0) Telemetry.ImportFailed.Add(failed);
            Telemetry.ImportApplyDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            if (!cancelled)
                await events.PublishAsync(
                    new VarsImported(runId, session.TargetRepositoryId, copied, fixedCount, renamed, activated),
                    cancellationToken).ConfigureAwait(false);

            if (cancelled)
                return new ApplyResult(copied, fixedCount, renamed, skipped, discarded, failed, runId, copiedIncoming, Cancelled: true);

            return new ApplyResult(copied, fixedCount, renamed, skipped, discarded, failed, runId, copiedIncoming);
        }
        finally
        {
            progress?.Report(new ProgressReport(1, 1, "Cleaning up…"));
            try { if (Directory.Exists(session.TempRoot)) Directory.Delete(session.TempRoot, recursive: true); }
            catch { /* startup sweep catches leftovers */ }
        }
    }

    private static bool IsCopyDecision(ImportDecision d) => d is
        ImportDecision.Import or ImportDecision.KeepIncoming or ImportDecision.ImportAndFix
        or ImportDecision.RenameToMeta or ImportDecision.KeepBoth;

    /// <summary>Free-space guard for Apply (§11): the target drive must hold the planned copies. Fails open if unknown.</summary>
    private static bool HasFreeSpace(string mount, long neededBytes)
    {
        if (neededBytes <= 0) return true;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(mount));
            if (string.IsNullOrEmpty(root)) return true;
            // A little headroom so we never fill the drive to the last byte.
            return new DriveInfo(root).AvailableFreeSpace >= neededBytes + (64L * 1024 * 1024);
        }
        catch { return true; }
    }

    /// <summary>Record the on-disk identity of an imported var (its final var-name) for activate-after. (5.9)</summary>
    private static void TrackImported(List<string> refs, ImportItem item, string landedFileName)
    {
        var name = Path.GetFileNameWithoutExtension(landedFileName);
        if (!string.IsNullOrWhiteSpace(name))
            refs.Add(name);
    }

    /// <summary>
    /// Remember loose source paths that were successfully copied (for Trash originals). Skips archive extracts
    /// under the session temp root and anything under a link-farm directory.
    /// </summary>
    private static void TrackCopiedIncoming(List<string> copiedIncoming, ImportSession session, ImportItem item)
    {
        var path = item.IncomingPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!string.IsNullOrEmpty(session.TempRoot)
            && path.StartsWith(session.TempRoot, StringComparison.OrdinalIgnoreCase))
            return;
        if (!LooseVarEnumerator.IsRealFile(path))
            return;
        if (!string.IsNullOrWhiteSpace(item.SourcePath)
            && Directory.Exists(item.SourcePath)
            && LooseVarEnumerator.IsUnderLinkDirectory(item.SourcePath, path))
            return;
        if (!copiedIncoming.Contains(path, StringComparer.OrdinalIgnoreCase))
            copiedIncoming.Add(path);
    }

    /// <summary>
    /// Activate the just-imported vars into VaM by accumulating them into a durable "Imported" loading preset and
    /// building its profile links. Idempotent + additive (already-present members aren't re-added); a no-op when the
    /// VaM path isn't configured (the activation service logs + returns without linking). Returns true if it ran. (5.9)
    /// </summary>
    private async Task<bool> ActivateImportedAsync(IReadOnlyList<string> refs, CancellationToken ct)
    {
        var presetId = await EnsureImportedPresetAsync(refs, ct).ConfigureAwait(false);
        if (presetId is not { } id)
            return false;
        var r = await activation.BuildProfileLinksAsync(id, ct).ConfigureAwait(false);
        return r.PrivilegeFailures == 0;
    }

    private async Task<long?> EnsureImportedPresetAsync(IReadOnlyList<string> refs, CancellationToken ct)
    {
        var existing = (await presets.ListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Name, ImportedPresetName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            var created = await presets.CreateAsync(ImportedPresetName, refs, ct).ConfigureAwait(false);
            return created.IsSuccess ? created.Value.Id : null;
        }

        // Additive: fold current members so re-imports don't duplicate rows.
        var current = (await presets.MembersAsync(existing.Id, ct).ConfigureAwait(false))
            .Select(IdentityFold.Compute).ToHashSet(StringComparer.Ordinal);
        foreach (var r in refs)
            if (current.Add(IdentityFold.Compute(r)))
                await presets.AddMemberAsync(existing.Id, r, ct).ConfigureAwait(false);
        return existing.Id;
    }

    public Task<IReadOnlyList<ImportRun>> HistoryAsync(int take, CancellationToken cancellationToken = default) =>
        history.RecentAsync(take, cancellationToken);

    public Task<PageResult<ImportOutcome>> HistoryOutcomesPageAsync(
        Guid runId,
        PageRequest request,
        string filter = "all",
        string? searchText = null,
        CancellationToken cancellationToken = default) =>
        history.OutcomesPageAsync(runId, request, filter, searchText, cancellationToken);

    /// <summary>
    /// Startup sweep (§6/E5): remove import temp-session dirs a crash left behind. Temp bases depend on the
    /// <c>import.temp_dir</c> setting + each repo's drive (§6/D4), so sweep every candidate base.
    /// </summary>
    public async Task SweepTempWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var setting = await settings.GetAsync(TempDirKey, cancellationToken).ConfigureAwait(false);
        var mounts = await db.Repositories.Select(r => r.MountPath).ToListAsync(cancellationToken).ConfigureAwait(false);

        var bases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Import.TempWorkspace.ResolveBase(setting, null),   // setting override, else %LOCALAPPDATA%
        };
        foreach (var mount in mounts)
            bases.Add(Import.TempWorkspace.ResolveBase(setting, mount)); // per target-drive base

        foreach (var b in bases)
            Import.TempWorkspace.SweepOrphans(b);
    }

    private async Task WaitForIndexAsync(Guid repositoryId, CancellationToken cancellationToken, IProgressSink? progress = null)
    {
        var start = await indexer.StartIndexRepositoryAsync(repositoryId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (start.IsFailure)
            throw new InvalidOperationException(start.Error.Message);

        while (!cancellationToken.IsCancellationRequested)
        {
            var status = await indexer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsFailure)
                throw new InvalidOperationException(status.Error.Message);
            var s = status.Value;
            progress?.Report(new ProgressReport(
                s.Done, Math.Max(1, s.Total),
                s.PhaseMessage ?? $"Indexing… ({s.State})"));
            if (s.State is IndexerJobState.Completed or IndexerJobState.Failed
                or IndexerJobState.Cancelled or IndexerJobState.Idle)
            {
                if (s.State == IndexerJobState.Failed)
                    throw new InvalidOperationException(s.Error ?? "index failed");
                return;
            }
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Copy one incoming var into the repo, optionally fixing CJK encoding on the way. Never overwrites. (§7)</summary>
    private async Task<(bool Ok, bool Fixed)> CopyItemAsync(ImportItem item, string target, bool fix, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.IncomingPath) || !File.Exists(item.IncomingPath) || File.Exists(target))
            return (false, false);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        if (fix && item.Signals.GbkEntryCount > 0 && item.Signals.Codepage is { } cpName
            && EncodingHealthEngine.CodePageFor(cpName) is int page)
        {
            var fixResult = await fixer.FixAsync(item.IncomingPath, page, target, ct).ConfigureAwait(false);
            if (fixResult.IsSuccess)
                return (true, true);
            // fix failed → import the original as-is (§7 · E2)
        }

        var copy = await mover.CopyVerifyRenameAsync(item.IncomingPath, target, ct).ConfigureAwait(false);
        return (copy.IsSuccess, false);
    }

    private static string RenameTarget(ImportItem item)
    {
        var meta = item.Signals.MetaIdentity ?? Path.GetFileNameWithoutExtension(item.FileName);
        var parsed = PackageId.TryParse(item.FileName);
        var version = parsed.IsSuccess ? parsed.Value.VersionToken : "1";
        return $"{meta}.{version}.var";
    }

    private static string Uniquify(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var p = Path.Combine(dir, $"{name} ({n}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    private static string SummarizeSources(IReadOnlyList<ImportSource> sources)
    {
        var folders = sources.Count(s => s.Kind == ImportSourceKind.Folder);
        var archives = sources.Count(s => s.Kind == ImportSourceKind.Archive);
        return $"{folders} folder(s), {archives} archive(s)";
    }
}
