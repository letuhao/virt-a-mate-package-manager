using VarVault.Common;
using VarVault.Sdk.Events;
using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Import;

/// <summary>How an incoming var relates to the library. (doc 30 §3.)</summary>
public enum ImportLane { New, Exact, Cjk, Conflict, Naming, Corrupt }

/// <summary>What to do with an import item. (doc 30 §5/§7.)</summary>
public enum ImportDecision { None, Import, Skip, KeepIncoming, KeepExisting, KeepBoth, RenameToMeta, ImportAndFix, Discard }

/// <summary>A source the user pointed at. (doc 30 §5/§6.)</summary>
public enum ImportSourceKind { Folder, Archive }

/// <summary>Outcome of preparing a source (extraction). (doc 30 §6.)</summary>
public enum ImportSourceStatus { Ok, PasswordProtected, CorruptArchive, NotEnoughSpace }

/// <summary>Everything the resolver/gallery shows for one var — derived from <c>IVarInspector</c>. (doc 30 §5.)</summary>
public sealed record ImportSignals(
    bool ValidZip,
    string IntegrityStatus,
    long SizeBytes,
    int EntryCount,
    DateTime FileMtime,
    bool HasPreview,
    string? PreviewThumbPath,
    string FilenameIdentity,
    string? MetaIdentity,
    bool MetaDivergent,
    string? Codepage,
    int GbkEntryCount,
    bool HasDuplicateEntries = false,
    string? DuplicateDetail = null);

/// <summary>A repo var an incoming item collides/matches with (Conflict/Exact). (doc 30 §5.)</summary>
public sealed record ExistingRef(long VarFileId, int Tier, string RepositoryName, string Path, ImportSignals Signals);

/// <summary>One inside-the-var difference between incoming and existing (name+CRC+size based). (doc 30 §4.)</summary>
public sealed record EntryDelta(char Kind /* + - ~ */, string Path);

/// <summary>One scanned var + its classification, signals, recommendation and (mutable) decision. (doc 30 §5.)</summary>
public sealed record ImportItem(
    Guid Id,
    string SourceLabel,
    string SourcePath,
    string FileName,
    string IncomingPath,   // the actual .var on disk (in the source folder or extracted temp) — used by Apply
    string IdentityKey,
    ImportLane Lane,
    ImportSignals Signals,
    ImportDecision Recommendation,
    string Reason,
    ExistingRef? Existing,
    IReadOnlyList<EntryDelta> Diff)
{
    /// <summary>The user's decision (defaults to auto lanes' recommendation; review lanes start <see cref="ImportDecision.None"/>).</summary>
    public ImportDecision Decision { get; set; }
}

/// <summary>A source folder/archive and how it fared. (doc 30 §6.)</summary>
public sealed record ImportSource(
    string Path,
    ImportSourceKind Kind,
    ImportSourceStatus Status,
    int VarCount,
    string? FailReason);

/// <summary>
/// Post-apply install mode for Import &amp; Review. Off by default.
/// <see cref="ImportedCopied"/> is the sealed D2 path; <see cref="ActiveSession"/> is the QoL that installs
/// every resolvable identity from the scan into the active loading preset.
/// </summary>
public enum ImportActivateMode
{
    /// <summary>Do not install anything after Apply.</summary>
    Off = 0,

    /// <summary>Only successfully copied/renamed vars → durable "Imported" loading preset + build links (D2/5.9).</summary>
    ImportedCopied = 1,

    /// <summary>
    /// Copied vars plus Exact/Skip/KeepExisting identities already in the library → active loading preset
    /// (same destination as installed-deps repair). Does not switch AddonPackages.
    /// </summary>
    ActiveSession = 2,
}

/// <summary>What to import and where. Target = a repository (its tier is the repo's). (doc 30 §5.)</summary>
public sealed record ImportSpec(
    IReadOnlyList<string> Paths,
    Guid TargetRepositoryId,
    ImportActivateMode ActivateMode = ImportActivateMode.Off);

/// <summary>A scanned, not-yet-applied import session (decisions mutate in place before Apply). (doc 30 §5.)</summary>
public sealed record ImportSession(
    Guid Id, string TempRoot, Guid TargetRepositoryId, ImportActivateMode ActivateMode,
    IReadOnlyList<ImportSource> Sources, IReadOnlyList<ImportItem> Items,
    IReadOnlyList<string> Warnings)   // D1 dedup-trust warnings (offline / unindexed target repo). (doc 30 §3/D1.)
{
    /// <summary>Back-compat / test convenience: a session with no dedup-trust warnings.</summary>
    public ImportSession(Guid id, string tempRoot, Guid targetRepositoryId, ImportActivateMode activateMode,
        IReadOnlyList<ImportSource> sources, IReadOnlyList<ImportItem> items)
        : this(id, tempRoot, targetRepositoryId, activateMode, sources, items, []) { }
}

/// <summary>Result of applying a session. (doc 30 §5.)</summary>
/// <param name="CopiedIncomingPaths">
/// Absolute paths of loose source <c>.var</c> files eligible for profile tidy / Trash originals:
/// successfully durable-copied files, Exact-skip dups already in the library, and KeepExisting —
/// only when a surviving repo/landed copy exists on disk (catalog alone is not trusted).
/// Excludes archive extracts under the session temp root and symlink/link-farm paths.
/// </param>
/// <param name="Cancelled">True when the apply stopped early on a graceful cancel (partial copies may still be listed).</param>
/// <param name="IndexWarning">Non-null when post-copy indexing/activate deferred with an error (copies still landed).</param>
public sealed record ApplyResult(
    int Copied,
    int Fixed,
    int Renamed,
    int Skipped,
    int Discarded,
    int Failed,
    Guid RunId,
    IReadOnlyList<string>? CopiedIncomingPaths = null,
    bool Cancelled = false,
    string? IndexWarning = null);

/// <summary>A failed source recorded in history (password/corrupt/space). (doc 30 §8.)</summary>
public sealed record ImportFailedSource(string Path, ImportSourceKind Kind, string Reason);

/// <summary>One applied item's outcome, persisted per run (doc 30 §8 · ImportOutcomeEntity).</summary>
public sealed record ImportOutcome(
    string FileName, string IdentityKey, ImportLane Lane, ImportDecision Decision, bool Ok, string? Reason);

/// <summary>A persisted import run for the History view. (doc 30 §8.)</summary>
public sealed record ImportRun(
    Guid Id,
    DateTime StartedUtc,
    Guid TargetRepositoryId,
    string SourceSummary,
    int Copied,
    int Fixed,
    int Renamed,
    int Skipped,
    int Discarded,
    int Failed,
    IReadOnlyList<ImportFailedSource> FailedSources,
    IReadOnlyList<ImportOutcome> Outcomes);

/// <summary>Result of extracting one archive to a temp dir. (doc 30 §6.)</summary>
public sealed record ArchiveExtractResult(bool Success, int ExtractedCount, ImportSourceStatus Status, string? FailReason);

/// <summary>
/// Raised after a successful import apply: vars have landed in the target repo + been indexed. Lets other
/// modules react (refresh dashboards, live feeds, activation). Past-tense per the event convention.
/// (doc 31 Phase 5.7.)
/// </summary>
public sealed record VarsImported(
    Guid RunId,
    Guid TargetRepositoryId,
    int Copied,
    int Fixed,
    int Renamed,
    bool ActivatedAfter) : IDomainEvent;

/// <summary>
/// Scans folders + archives into a classified <see cref="ImportSession"/> (read-only), applies the user's decisions
/// (durable copy + fix + rename + quarantine + history + temp cleanup), and reads import history. (doc 30 §5.)
/// </summary>
public interface IImportService
{
    Task<ImportSession> ScanAsync(ImportSpec spec, IProgressSink? progress = null, CancellationToken cancellationToken = default);
    Task<ApplyResult> ApplyAsync(ImportSession session, IProgressSink? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportRun>> HistoryAsync(int take, CancellationToken cancellationToken = default);
    Task<PageResult<ImportOutcome>> HistoryOutcomesPageAsync(
        Guid runId,
        PageRequest request,
        string filter = "all",
        string? searchText = null,
        CancellationToken cancellationToken = default);

    /// <summary>Delete import temp-workspace dirs orphaned by a crashed run (called once at startup). (doc 30 §6/E5.)</summary>
    Task SweepTempWorkspacesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Extracts an archive (zip/7z/rar/tar) to a directory; reports password/corrupt without throwing. (doc 30 §6.)</summary>
public interface IArchiveExtractor
{
    Task<ArchiveExtractResult> ExtractAsync(string archivePath, string destDir, CancellationToken cancellationToken = default);
}

/// <summary>A scoped temp directory that auto-cleans on dispose. (doc 30 §6.)</summary>
public interface ITempWorkspace : IAsyncDisposable
{
    string Root { get; }
    string NewDir(string name);
}

/// <summary>Persists + reads import runs (History). (doc 30 §8.)</summary>
public interface IImportHistoryStore
{
    Task<Guid> RecordAsync(ImportRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ImportRun>> RecentAsync(int take, CancellationToken cancellationToken = default);
    Task<PageResult<ImportOutcome>> OutcomesPageAsync(
        Guid runId,
        PageRequest request,
        string filter = "all",
        string? searchText = null,
        CancellationToken cancellationToken = default);
}
