using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Services;
using VarVault.Common;
using VarVault.Domain.Indexing;
using VarVault.Domain.Safety;
using VarVault.Sdk.Import;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;

namespace VarVault.App.ViewModels;

/// <summary>One scanned var in the import review list; wraps the SDK <see cref="ImportItem"/> with an observable
/// decision so the grid/gallery + resolver stay in sync. (doc 31 Phase 6.1.)</summary>
public sealed partial class ImportItemViewModel(ImportItem item) : ObservableObject
{
    public ImportItem Model { get; } = item;
    public string FileName => Model.FileName;
    public string Creator => Model.SourceLabel;
    public ImportLane Lane => Model.Lane;
    public ImportSignals Signals => Model.Signals;
    public ExistingRef? Existing => Model.Existing;
    public string Reason => Model.Reason;
    public ImportDecision Recommendation => Model.Recommendation;
    public System.Collections.Generic.IReadOnlyList<EntryDelta> Diff => Model.Diff;
    public bool HasDiff => Diff.Count > 0;

    public string SizeLabel => Common.Formatting.ByteSize.Humanize(Model.Signals.SizeBytes);
    public bool NeedsReview => ImportViewModel.IsReviewLane(Lane);
    public int GbkCount => Model.Signals.GbkEntryCount;

    // Per-lane flags drive the resolver's lane-appropriate section + decision buttons (draft §10 · G1).
    public bool IsNew => Lane == ImportLane.New;
    public bool IsExact => Lane == ImportLane.Exact;
    public bool IsCjk => Lane == ImportLane.Cjk;
    public bool IsConflict => Lane == ImportLane.Conflict;
    public bool IsNaming => Lane == ImportLane.Naming;
    public bool IsCorrupt => Lane == ImportLane.Corrupt;
    public bool IsAuto => !NeedsReview;   // New / Exact / CJK are auto-decided

    // Naming lane: filename-derived identity vs the var's own meta.json identity (G2 · D3).
    public string FilenameIdentity => Model.Signals.FilenameIdentity;
    public string MetaIdentityLabel => Model.Signals.MetaIdentity ?? "(unreadable)";
    public bool HasExisting => Existing is not null;
    public string IntegrityStatus => Model.Signals.IntegrityStatus;
    public int EntryCount => Model.Signals.EntryCount;

    /// <summary>Decision-state pill for the list (draft): auto lanes show the lane; review lanes show decided/pending.</summary>
    public string ListPillText => NeedsReview ? (IsResolved ? "✓ " + DecisionLabel : "needs review") : LaneLabel;
    public string LaneLabel => Lane switch
    {
        ImportLane.New => "New", ImportLane.Exact => "Exact", ImportLane.Cjk => "CJK",
        ImportLane.Conflict => "Conflict", ImportLane.Naming => "Name≠meta", _ => "Corrupt",
    };

    /// <summary>Absolute path of the preview image extracted from the incoming var (null → placeholder). (6.3)</summary>
    public string? PreviewPath => Model.Signals.PreviewThumbPath;
    public bool HasPreview => Model.Signals.HasPreview;

    [ObservableProperty] private ImportDecision _decision = item.Decision;

    partial void OnDecisionChanged(ImportDecision value)
    {
        Model.Decision = value;
        OnPropertyChanged(nameof(DecisionLabel));
        OnPropertyChanged(nameof(IsResolved));
        OnPropertyChanged(nameof(ListPillText));
    }

    /// <summary>Resolver button hook: set this item's decision by enum name. (doc 31 Phase 6.4.)</summary>
    [RelayCommand]
    private void Choose(string decision)
    {
        if (Enum.TryParse<ImportDecision>(decision, out var d))
            Decision = d;
    }

    public bool IsResolved => Decision != ImportDecision.None;
    public string DecisionLabel => Decision switch
    {
        ImportDecision.Import => "Import", ImportDecision.ImportAndFix => "Import + fix",
        ImportDecision.KeepIncoming => "Keep new", ImportDecision.KeepExisting => "Keep existing",
        ImportDecision.KeepBoth => "Keep both", ImportDecision.RenameToMeta => "Rename to meta",
        ImportDecision.Skip => "Skip", ImportDecision.Discard => "Discard", _ => "",
    };
}

/// <summary>One immutable row in an import-history detail report.</summary>
public sealed class ImportOutcomeRow(ImportOutcome outcome)
{
    public ImportOutcome Model { get; } = outcome;
    public string FileName => Model.FileName;
    public string IdentityKey => Model.IdentityKey;
    public string Lane => Model.Lane.ToString();
    public string Decision => Model.Decision.ToString();
    public string Reason => string.IsNullOrWhiteSpace(Model.Reason) ? "Completed" : Model.Reason!;
    public bool IsFailed => !Model.Ok;
    public string Status => IsFailed ? "Failed" : Model.Reason switch
    {
        "skipped" or "cancelled" => "Skipped",
        "discarded" => "Discarded",
        _ => "Completed",
    };
}

/// <summary>
/// SCR · Import &amp; review screen (doc 30 §10). Pick source folders/archives + a target repo, scan (classify), review
/// only the conflict/naming/corrupt lanes (table or gallery + resolver), then apply. (doc 31 Phase 6.)
/// </summary>
public sealed partial class ImportViewModel : ObservableObject, ILoadableScreen
{
    private readonly IImportService _import;
    private readonly IRepositoryService _repos;
    private readonly ImportJobRunner _jobs;
    private readonly IUiDispatcher _ui;
    private readonly IAddonPackagesLooseVarsLocator? _looseLocator;
    private readonly ITrashService? _trash;

    private JobHandle? _activeJob;
    private CancellationTokenSource? _progressPollCts;
    private readonly List<string> _pendingTrashOriginals = [];

    public ImportViewModel(
        IImportService import,
        IRepositoryService repos,
        ImportJobRunner jobs,
        IUiDispatcher ui,
        IAddonPackagesLooseVarsLocator? looseLocator = null,
        ITrashService? trash = null)
    {
        _import = import;
        _repos = repos;
        _jobs = jobs;
        _ui = ui;
        _looseLocator = looseLocator;
        _trash = trash;
        Pager = new PagedListState<ImportItemViewModel>(LoadPageAsync);
        HistoryOutcomesPager = new PagedListState<ImportOutcomeRow>(LoadHistoryOutcomePageAsync, defaultPageSize: 25);
    }

    public static bool IsReviewLane(ImportLane l) => l is ImportLane.Conflict or ImportLane.Naming or ImportLane.Corrupt;

    private readonly List<ImportItemViewModel> _all = [];
    private ImportSession? _session;
    public PagedListState<ImportItemViewModel> Pager { get; }

    public ObservableCollection<RepositoryInfo> Repositories { get; } = [];
    [ObservableProperty] private RepositoryInfo? _targetRepo;

    public ObservableCollection<string> SourcePaths { get; } = [];
    public ObservableCollection<ImportSource> Sources { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];   // D1 dedup-trust warnings (offline/unindexed repo).
    public bool HasWarnings => Warnings.Count > 0;
    /// <summary>True when the user has picked sources that have not been scanned yet (chips strip).</summary>
    public bool HasPendingSources => SourcePaths.Count > 0 && !HasSession;
    public bool HasScannedSources => Sources.Count > 0;
    public ObservableCollection<ImportItemViewModel> Items => Pager.Items;   // the filtered view
    public ObservableCollection<ImportRun> History { get; } = [];
    [ObservableProperty] private ImportRun? _selectedHistoryRun;
    [ObservableProperty] private string _historyOutcomeFilter = "all";
    [ObservableProperty] private string _historyOutcomeSearch = "";
    public PagedListState<ImportOutcomeRow> HistoryOutcomesPager { get; }

    /// <summary>True when the history overlay is showing a run's detail report.</summary>
    public bool HasHistoryDetail => SelectedHistoryRun is not null;

    public bool SelectedHasFailedSources => (SelectedHistoryRun?.FailedSources.Count ?? 0) > 0;
    public string SelectedHistoryTitle => SelectedHistoryRun is { } r
        ? $"Run {r.StartedUtc:g} — {r.SourceSummary}"
        : "Select a run";
    public string SelectedHistoryCounts => SelectedHistoryRun is { } r
        ? $"✓ {r.Copied} copied · ✏ {r.Fixed} fixed · 🏷 {r.Renamed} renamed · ↷ {r.Skipped} skipped · 🗑 {r.Discarded} discarded · ✗ {r.Failed} failed"
        : "";

    [ObservableProperty] private string _laneFilter = "all";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _galleryView;
    [ObservableProperty] private ImportItemViewModel? _selected;
    [ObservableProperty] private bool _activateAfter;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _historyOpen;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private double _operationProgress;
    [ObservableProperty] private string? _operationMessage;

    public bool IsOperationRunning => IsScanning || IsApplying;
    public bool CanCancelOperation => _activeJob is { State: JobState.Queued or JobState.Running };
    public bool CanEditSources => !IsApplying;
    public bool CanEditReview => !IsApplying;

    /// <summary>Folder picker hook set by the view (real Avalonia StorageProvider). Returns chosen folder paths.</summary>
    public Func<Task<IReadOnlyList<string>>>? FolderPicker { get; set; }

    /// <summary>Archive-file picker hook set by the view (real Avalonia StorageProvider). Returns chosen archive paths.</summary>
    public Func<Task<IReadOnlyList<string>>>? ArchivePicker { get; set; }

    // Triage counts (recomputed after scan / decision changes).
    public int TotalScanned => _all.Count;
    public int NewCount => _all.Count(i => i.Lane == ImportLane.New);
    public int CjkCount => _all.Count(i => i.Lane == ImportLane.Cjk);
    public int ExactCount => _all.Count(i => i.Lane == ImportLane.Exact);
    public int NamingCount => _all.Count(i => i.Lane == ImportLane.Naming);
    public int ConflictCount => _all.Count(i => i.Lane == ImportLane.Conflict);
    public int CorruptCount => _all.Count(i => i.Lane == ImportLane.Corrupt);
    public int ReviewTotal => _all.Count(i => i.NeedsReview);
    public int ReviewResolved => _all.Count(i => i.NeedsReview && i.IsResolved);
    public int ReviewRemaining => ReviewTotal - ReviewResolved;
    public double ReviewProgress => ReviewTotal == 0 ? 1 : (double)ReviewResolved / ReviewTotal;
    public bool HasSession => _session is not null;

    /// <summary>Sources strip summary: pending picks before scan, extracted counts after.</summary>
    public string SourcesSummary
    {
        get
        {
            if (!HasSession)
            {
                if (SourcePaths.Count == 0)
                    return "No sources yet — add a folder or archive, then Scan.";
                return $"{SourcePaths.Count} source(s) ready — click Scan to classify.";
            }
            var folders = Sources.Count(s => s.Kind == ImportSourceKind.Folder);
            var archives = Sources.Count(s => s.Kind == ImportSourceKind.Archive && s.Status == ImportSourceStatus.Ok);
            var failed = Sources.Count(s => s.Status != ImportSourceStatus.Ok);
            var s = $"{folders} folder(s) · {archives} archive(s) extracted";
            return failed > 0 ? s + $" · 🔒 {failed} failed/password (logged to History)" : s;
        }
    }

    /// <summary>Why Scan/Apply are gated — shown so buttons don't look "dead".</summary>
    public string GateHint
    {
        get
        {
            if (IsScanning)
                return OperationMessage ?? "Scanning…";
            if (IsApplying)
                return OperationMessage ?? "Applying…";
            if (Repositories.Count == 0)
                return "Add a repository first (Repositories screen), then pick an Import into target.";
            if (TargetRepo is null)
                return "Choose a target repository (Import into).";
            if (SourcePaths.Count == 0)
                return "Add at least one folder or archive — or use Quick import (profile) for in-game downloads.";
            if (_session is not null && ReviewRemaining > 0 && CanApply)
                return $"{ReviewRemaining} undecided — Apply will skip them (keep repo / don’t import). Or Accept recommendations.";
            if (_session is not null && ReviewRemaining > 0)
                return $"{ReviewRemaining} item(s) still need a decision before Apply.";
            if (_session is not null && CanApply)
                return "Ready to Apply.";
            if (CanScan)
                return "Ready to Scan.";
            return "";
        }
    }

    // Live apply plan (action-bar note "Copy N · fix M · skip K · discard L"). (G8)
    private static readonly ImportDecision[] CopyDecisions =
        [ImportDecision.Import, ImportDecision.ImportAndFix, ImportDecision.KeepIncoming, ImportDecision.KeepBoth, ImportDecision.RenameToMeta];
    public int CopyPlanned => _all.Count(i => CopyDecisions.Contains(i.Decision));
    public int FixPlanned => _all.Count(i => i.Decision == ImportDecision.ImportAndFix
        || (i.GbkCount > 0 && (i.Decision is ImportDecision.Import or ImportDecision.KeepIncoming or ImportDecision.KeepBoth or ImportDecision.RenameToMeta)));
    public int SkipPlanned => _all.Count(i => i.Decision is ImportDecision.Skip or ImportDecision.KeepExisting or ImportDecision.None);
    public int DiscardPlanned => _all.Count(i => i.Decision == ImportDecision.Discard);
    public string ApplyPlanSummary =>
        $"Copy {CopyPlanned} · fix {FixPlanned} · skip {SkipPlanned} · discard {DiscardPlanned}";
    /// <summary>
    /// Apply is available as soon as a scan exists. Undecided review items are treated as a lane-safe
    /// skip at Apply time (Conflict→keep repo, Naming→skip, Corrupt→discard) so users can decide only
    /// the vars they care about.
    /// </summary>
    public bool CanApply => _session is not null && !IsApplying && !IsScanning && _all.Count > 0;
    public bool CanScan => TargetRepo is not null && SourcePaths.Count > 0 && !IsScanning && !IsApplying;
    public bool HasUndecidedReview => ReviewRemaining > 0;

    /// <summary>When true (default for Quick import sessions), trash successfully copied profile originals after Apply.</summary>
    [ObservableProperty] private bool _trashOriginalsAfterApply;

    [ObservableProperty] private string? _quickImportHint;
    [ObservableProperty] private bool _isRefreshingQuickImport;

    public bool CanQuickImport =>
        _looseLocator is not null
        && TargetRepo is not null
        && !IsScanning
        && !IsApplying
        && CanEditSources;

    public bool CanTrashOriginals =>
        _trash is not null
        && _pendingTrashOriginals.Count > 0
        && !IsScanning
        && !IsApplying;

    public int PendingTrashOriginalsCount => _pendingTrashOriginals.Count;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Repositories.Clear();
        foreach (var r in await _repos.ListAsync(cancellationToken).ConfigureAwait(true))
            Repositories.Add(r);
        TargetRepo ??= Repositories.FirstOrDefault(r => r.Tier == Repositories.Min(x => x.Tier)) ?? Repositories.FirstOrDefault();
        await RefreshHistoryAsync(cancellationToken).ConfigureAwait(true);
        await RefreshQuickImportHintAsync(cancellationToken).ConfigureAwait(true);
        NotifyGate();
    }

    /// <summary>Refresh the Quick import status line (loose vars in the active profile).</summary>
    [RelayCommand]
    public async Task RefreshQuickImportHintAsync(CancellationToken cancellationToken = default)
    {
        if (_looseLocator is null)
        {
            QuickImportHint = null;
            NotifyQuickImport();
            return;
        }

        IsRefreshingQuickImport = true;
        try
        {
            var locate = await _looseLocator.LocateAsync(cancellationToken).ConfigureAwait(true);
            QuickImportHint = locate.Hint;
        }
        catch (Exception ex)
        {
            QuickImportHint = $"Can't read active profile: {ex.Message}";
        }
        finally
        {
            IsRefreshingQuickImport = false;
            NotifyQuickImport();
        }
    }

    /// <summary>
    /// One-click source = active VaM profile folder (in-game downloads next to symlinks), then Scan.
    /// Symlink farms are skipped by the import enumerator.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanQuickImport))]
    public async Task QuickImportAsync(CancellationToken cancellationToken = default)
    {
        if (_looseLocator is null || TargetRepo is null)
            return;

        var locate = await _looseLocator.LocateAsync(cancellationToken).ConfigureAwait(true);
        QuickImportHint = locate.Hint;
        if (locate.Status != LooseVarsLocateStatus.Ready || string.IsNullOrWhiteSpace(locate.ProfilePath))
        {
            StatusMessage = locate.Hint;
            NotifyQuickImport();
            return;
        }

        if (locate.LooseVarCount == 0)
        {
            StatusMessage = locate.Hint;
            NotifyQuickImport();
            return;
        }

        // Replace pending sources with the profile path only (tidy session).
        if (IsApplying)
            return;
        if (_session is not null)
            ClearSessionKeepSources();
        SourcePaths.Clear();
        SourcePaths.Add(locate.ProfilePath);
        TrashOriginalsAfterApply = true;
        NotifyPendingSources();
        StatusMessage = $"Quick import: {locate.LooseVarCount} loose var(s) from “{locate.ProfileName}”.";
        NotifyGate();
        await ScanAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Move successfully imported loose profile <c>.var</c> originals into VarVault Trash (restorable).
    /// Never touches symlinks or link-farm dirs. Explicit tidy — does not change Discard semantics.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTrashOriginals))]
    public async Task TrashOriginalsAsync(CancellationToken cancellationToken = default)
    {
        if (_trash is null || _pendingTrashOriginals.Count == 0)
            return;

        var trashed = 0;
        var failed = 0;
        foreach (var path in _pendingTrashOriginals.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeToTrashOriginal(path))
            {
                failed++;
                continue;
            }

            var result = await _trash.TrashAsync(path, "import.tidy-original", cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                trashed++;
                _pendingTrashOriginals.Remove(path);
            }
            else
                failed++;
        }

        StatusMessage = failed == 0
            ? $"Trashed {trashed} original{(trashed == 1 ? "" : "s")} from profile."
            : $"Trashed {trashed} · skipped/failed {failed} (symlinks and missing files are left alone).";
        await RefreshQuickImportHintAsync(cancellationToken).ConfigureAwait(true);
        NotifyTrashOriginals();
    }

    /// <summary>"+ Folder…" — pick one or more source folders via the native OS dialog. (QoL)</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (FolderPicker is null)
        {
            StatusMessage = "Folder picker is not available in this host.";
            return;
        }
        var paths = await FolderPicker().ConfigureAwait(true);
        AddPaths(paths, "folder");
    }

    /// <summary>"+ Archive…" — pick one or more archive files (zip/7z/rar/tar) via the native OS dialog. (QoL)</summary>
    [RelayCommand]
    private async Task AddArchiveAsync()
    {
        if (ArchivePicker is null)
        {
            StatusMessage = "Archive picker is not available in this host.";
            return;
        }
        var paths = await ArchivePicker().ConfigureAwait(true);
        AddPaths(paths, "archive");
    }

    private void AddPaths(IReadOnlyList<string> paths, string kindLabel)
    {
        if (IsApplying)
            return;
        var added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || SourcePaths.Contains(p))
                continue;
            SourcePaths.Add(p);
            added++;
        }
        // A new pick invalidates any prior unapplied session — user must re-scan.
        if (added > 0 && _session is not null)
            ClearSessionKeepSources();
        StatusMessage = added > 0
            ? $"Added {added} {kindLabel}(s) · {SourcePaths.Count} source(s) total"
            : paths.Count == 0
                ? "No path selected."
                : "Those sources were already on the list.";
        NotifyPendingSources();
        NotifyGate();
    }

    /// <summary>Add a source path directly (test seam / drag-drop).</summary>
    public void AddSourcePath(string path)
    {
        if (IsApplying)
            return;
        if (string.IsNullOrWhiteSpace(path) || SourcePaths.Contains(path))
            return;
        SourcePaths.Add(path);
        if (_session is not null)
            ClearSessionKeepSources();
        NotifyPendingSources();
        NotifyGate();
    }

    /// <summary>Remove a pending source chip before Scan.</summary>
    [RelayCommand]
    private void RemoveSource(string? path)
    {
        if (IsApplying)
            return;
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!SourcePaths.Remove(path))
            return;
        if (_session is not null)
            ClearSessionKeepSources();
        StatusMessage = SourcePaths.Count == 0
            ? "All sources removed."
            : $"Removed · {SourcePaths.Count} source(s) remaining";
        NotifyPendingSources();
        NotifyGate();
    }

    /// <summary>Drop scanned session/items when the source set changes; keep SourcePaths.</summary>
    private void ClearSessionKeepSources()
    {
        DiscardTemp(_session?.TempRoot);
        _session = null;
        foreach (var i in _all)
            i.PropertyChanged -= OnItemChanged;
        _all.Clear();
        Items.Clear();
        Sources.Clear();
        Warnings.Clear();
        Selected = null;
        OnPropertyChanged(nameof(HasWarnings));
        NotifyCounts();
        NotifyPendingSources();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        if (TargetRepo is null || SourcePaths.Count == 0)
            return;

        var spec = new ImportSpec([.. SourcePaths], TargetRepo.Id, ActivateAfter);
        var token = CaptureScanToken();

        IsScanning = true;
        OperationProgress = 0;
        OperationMessage = "Queued…";
        NotifyOperationState();
        try
        {
            var job = _jobs.StartScan(spec);
            _activeJob = job.Handle;
            NotifyOperationState(); // Cancel becomes enabled once a handle exists
            StartProgressMirror(job.Handle);

            var session = await job.Result.ConfigureAwait(true);
            if (!IsScanStillValid(token))
            {
                // Rejected result still owns a temp workspace — must not orphan it.
                DiscardTemp(session.TempRoot);
                StatusMessage = "Scan finished but inputs changed — run Scan again.";
                return;
            }

            await InstallSessionAsync(session).ConfigureAwait(true);
            StatusMessage = $"Scanned {_all.Count} vars · {ReviewRemaining} need review";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _session = null;
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            StopProgressMirror();
            _activeJob = null;
            IsScanning = false;
            OperationProgress = 0;
            OperationMessage = null;
            NotifyCounts();
            NotifyOperationState();
        }
    }

    [RelayCommand]
    private void AcceptAll()
    {
        if (IsApplying)
            return;
        foreach (var i in _all.Where(x => x.NeedsReview && !x.IsResolved))
        {
            // Prefer the recommender; fall back to a lane-safe skip when it has no default (shouldn't).
            i.Decision = i.Recommendation != ImportDecision.None
                ? i.Recommendation
                : SkipDecisionFor(i);
        }
        NotifyCounts();
        NotifyGate();
    }

    /// <summary>
    /// Leave already-chosen decisions alone; mark every remaining review item as a no-import skip
    /// (Conflict keeps the repo copy). One-click path for "I only care about these few".
    /// </summary>
    [RelayCommand]
    private void SkipRemaining()
    {
        if (IsApplying)
            return;
        ApplySkipToUndecided();
        NotifyCounts();
        NotifyGate();
    }

    /// <summary>Set one item's decision (from the resolver buttons). Param: "itemId|Decision".</summary>
    [RelayCommand]
    private void SetDecision(string param)
    {
        if (IsApplying)
            return;
        var parts = param.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id) || !Enum.TryParse<ImportDecision>(parts[1], out var dec))
            return;
        var vm = _all.FirstOrDefault(x => x.Model.Id == id);
        if (vm is not null)
            vm.Decision = dec;
        NotifyCounts();
        NotifyGate();
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    public async Task ApplyAsync()
    {
        if (_session is null)
            return;

        // Partial review is intentional UX: undecided review lanes become a safe skip before freeze.
        ApplySkipToUndecided();
        NotifyCounts();

        var snapshot = ImportJobRunner.FreezeSession(
            _session,
            _all.Select(i => (i.Model.Id, i.Decision)));

        IsApplying = true;
        OperationProgress = 0;
        OperationMessage = "Queued…";
        NotifyOperationState();
        try
        {
            var job = _jobs.StartApply(snapshot);
            _activeJob = job.Handle;
            NotifyOperationState();
            StartProgressMirror(job.Handle);

            var r = await job.Result.ConfigureAwait(true);
            CapturePendingTrashOriginals(r);
            StatusMessage = r.Cancelled
                ? $"Import cancelled — kept {r.Copied + r.Renamed} copied · skipped {r.Skipped} (logged to History)."
                : $"Done: copied {r.Copied} · fixed {r.Fixed} · renamed {r.Renamed} · skipped {r.Skipped} · discarded {r.Discarded}"
                  + (r.Failed > 0 ? $" · failed {r.Failed}" : "");
            ResetAfterApply();
            await RefreshHistoryAsync().ConfigureAwait(true);
            // Don't gate on CanTrashOriginals — that requires !IsApplying, and we are still applying until finally.
            if (TrashOriginalsAfterApply && _trash is not null && _pendingTrashOriginals.Count > 0)
                await TrashOriginalsAsync().ConfigureAwait(true);
            else
                NotifyTrashOriginals();
            await RefreshQuickImportHintAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Job-level cancel before Apply returns (rare); temp already cleaned by Apply finally when it ran.
            ResetAfterApply();
            StatusMessage = "Import cancelled — copied items are kept, the rest skipped (logged to History).";
            await RefreshHistoryAsync().ConfigureAwait(true);
            NotifyTrashOriginals();
        }
        catch (Exception ex)
        {
            // §11: target offline/full and other apply failures surface as a clear message, not a crash.
            StatusMessage = $"Can't import: {ex.Message}";
            // Mid-apply failures also delete TempRoot; a session pointing at deleted files is unusable.
            if (_session is not null && !string.IsNullOrEmpty(_session.TempRoot) && !Directory.Exists(_session.TempRoot))
            {
                ClearSessionKeepSources();
                StatusMessage += " · re-scan sources to try again.";
            }
        }
        finally
        {
            StopProgressMirror();
            _activeJob = null;
            IsApplying = false;
            OperationProgress = 0;
            OperationMessage = null;
            NotifyCounts();
            NotifyOperationState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation()
    {
        try { _activeJob?.Cancel(); }
        catch (ObjectDisposedException) { /* job already finished */ }
    }

    private sealed record ScanRequestToken(Guid TargetId, bool ActivateAfter, IReadOnlyList<string> Paths);

    private ScanRequestToken CaptureScanToken() =>
        new(TargetRepo!.Id, ActivateAfter, SourcePaths.ToList());

    private bool IsScanStillValid(ScanRequestToken token) =>
        TargetRepo?.Id == token.TargetId
        && ActivateAfter == token.ActivateAfter
        && SourcePaths.Count == token.Paths.Count
        && SourcePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(token.Paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

    private async Task InstallSessionAsync(ImportSession session)
    {
        // Replacing a prior unapplied session must not leave its temp dir behind.
        if (_session is not null && !string.Equals(_session.TempRoot, session.TempRoot, StringComparison.OrdinalIgnoreCase))
            DiscardTemp(_session.TempRoot);

        _session = session;
        foreach (var i in _all)
            i.PropertyChanged -= OnItemChanged;
        _all.Clear();
        foreach (var it in session.Items)
        {
            var vm = new ImportItemViewModel(it);
            vm.PropertyChanged += OnItemChanged;
            _all.Add(vm);
        }
        Sources.Clear();
        foreach (var s in session.Sources)
            Sources.Add(s);
        Warnings.Clear();
        foreach (var w in session.Warnings)
            Warnings.Add(w);
        OnPropertyChanged(nameof(SourcesSummary));
        OnPropertyChanged(nameof(HasWarnings));
        NotifyPendingSources();
        await ApplyFilterAsync().ConfigureAwait(true);
    }

    private void ResetAfterApply()
    {
        // TempRoot already deleted by ApplyAsync — don't double-delete.
        _session = null;
        foreach (var i in _all)
            i.PropertyChanged -= OnItemChanged;
        _all.Clear();
        Items.Clear();
        Sources.Clear();
        SourcePaths.Clear();
        Warnings.Clear();
        Selected = null;
        OnPropertyChanged(nameof(HasWarnings));
        NotifyPendingSources();
    }

    /// <summary>
    /// Remember successfully-copied loose source files (not archive extracts under TempRoot) for Trash originals.
    /// Unions with any prior pending paths so a second Apply does not drop untidy originals from the first.
    /// </summary>
    private void CapturePendingTrashOriginals(ApplyResult result)
    {
        foreach (var path in result.CopiedIncomingPaths ?? [])
        {
            if (!IsSafeToTrashOriginal(path))
                continue;
            if (!_pendingTrashOriginals.Contains(path, StringComparer.OrdinalIgnoreCase))
                _pendingTrashOriginals.Add(path);
        }
        NotifyTrashOriginals();
    }

    private static bool IsSafeToTrashOriginal(string path, string? sourceFolder = null)
    {
        if (!LooseVarEnumerator.IsRealFile(path))
            return false;
        if (!string.IsNullOrWhiteSpace(sourceFolder)
            && Directory.Exists(sourceFolder)
            && LooseVarEnumerator.IsUnderLinkDirectory(sourceFolder, path))
            return false;
        // Absolute safety: refuse any path whose segment is a link-farm name.
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (RepositoryScanRules.IsLinkDirectory(segment))
                return false;
        }
        return true;
    }

    private void NotifyQuickImport()
    {
        OnPropertyChanged(nameof(CanQuickImport));
        QuickImportCommand.NotifyCanExecuteChanged();
    }

    private void NotifyTrashOriginals()
    {
        OnPropertyChanged(nameof(CanTrashOriginals));
        OnPropertyChanged(nameof(PendingTrashOriginalsCount));
        TrashOriginalsCommand.NotifyCanExecuteChanged();
    }

    private static void DiscardTemp(string? root)
    {
        if (string.IsNullOrEmpty(root))
            return;
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch { /* startup sweep catches leftovers */ }
    }

    private void StartProgressMirror(JobHandle handle)
    {
        StopProgressMirror();
        _progressPollCts = new CancellationTokenSource();
        var ct = _progressPollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested
                   && handle.State is JobState.Queued or JobState.Running)
            {
                var p = handle.Progress;
                try
                {
                    await _ui.InvokeAsync(() =>
                    {
                        OperationProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                        if (!string.IsNullOrEmpty(p.Message))
                            OperationMessage = p.Message;
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (TaskCanceledException) { break; }
                try { await Task.Delay(200, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }, ct);
    }

    private void StopProgressMirror()
    {
        if (_progressPollCts is null)
            return;
        try { _progressPollCts.Cancel(); }
        catch { /* best-effort */ }
        _progressPollCts.Dispose();
        _progressPollCts = null;
    }

    private void NotifyOperationState()
    {
        OnPropertyChanged(nameof(IsOperationRunning));
        OnPropertyChanged(nameof(CanCancelOperation));
        OnPropertyChanged(nameof(CanEditSources));
        OnPropertyChanged(nameof(CanEditReview));
        NotifyGate();
        CancelOperationCommand.NotifyCanExecuteChanged();
    }

    partial void OnOperationMessageChanged(string? value) => OnPropertyChanged(nameof(GateHint));

    // ── Keyboard (doc 31 Phase 6.6 · 29-draft): J/K navigate, ] [ \ decide, Del discard. Called from the
    //    view's KeyDown. Navigation walks the visible list; decisions apply to the selected review item. ──────
    /// <summary>Move the selection within the visible list by <paramref name="delta"/> (J=+1, K=−1). (6.6)</summary>
    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
            return;
        var idx = Selected is null ? -1 : Items.IndexOf(Selected);
        var next = Math.Clamp(idx + delta, 0, Items.Count - 1);
        Selected = Items[next];
    }

    /// <summary>"]" — the primary decision for the selected review item (keep-incoming / rename). (6.6)</summary>
    public void DecidePrimary() => Decide(i => i.Lane switch
    {
        ImportLane.Conflict => ImportDecision.KeepIncoming,
        ImportLane.Naming => ImportDecision.RenameToMeta,
        _ => (ImportDecision?)null,
    });

    /// <summary>"[" — the secondary decision (keep-existing / keep original name). (6.6)</summary>
    public void DecideSecondary() => Decide(i => i.Lane switch
    {
        ImportLane.Conflict => ImportDecision.KeepExisting,
        ImportLane.Naming => ImportDecision.Import,
        _ => (ImportDecision?)null,
    });

    /// <summary>"\" — keep both (conflict only). (6.6)</summary>
    public void DecideBoth() => Decide(i => i.Lane == ImportLane.Conflict ? ImportDecision.KeepBoth : null);

    /// <summary>Del/Backspace — discard the selected review item. (6.6)</summary>
    public void DiscardSelected() => Decide(_ => ImportDecision.Discard);

    private void Decide(Func<ImportItemViewModel, ImportDecision?> pick)
    {
        if (IsApplying)
            return;
        if (Selected is not { NeedsReview: true } sel)
            return;
        if (pick(sel) is { } d)
        {
            sel.Decision = d;
            NotifyCounts();
            NotifyGate();
        }
    }

    private void ApplySkipToUndecided()
    {
        foreach (var i in _all.Where(x => x.NeedsReview && !x.IsResolved))
            i.Decision = SkipDecisionFor(i);
    }

    /// <summary>Lane-safe "don't change my library" default for an undecided review item.</summary>
    private static ImportDecision SkipDecisionFor(ImportItemViewModel i) => i.Lane switch
    {
        ImportLane.Conflict => ImportDecision.KeepExisting,
        ImportLane.Corrupt => ImportDecision.Discard,
        _ => ImportDecision.Skip,
    };

    [RelayCommand] private void ToggleView() => GalleryView = !GalleryView;
    [RelayCommand] private void Filter(string lane) { LaneFilter = lane; _ = ApplyFilterAsync(); }
    [RelayCommand]
    private async Task OpenHistory()
    {
        await RefreshHistoryAsync().ConfigureAwait(true);
        HistoryOpen = true;
        await SelectHistoryRunAsync(History.FirstOrDefault()).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseHistory()
    {
        HistoryOpen = false;
        SelectedHistoryRun = null;
        NotifyHistoryDetail();
    }

    /// <summary>Open the detail report for one history run (failed items + failed sources).</summary>
    [RelayCommand]
    private async Task OpenHistoryDetailAsync(ImportRun? run)
    {
        await SelectHistoryRunAsync(run).ConfigureAwait(true);
    }

    /// <summary>History "Retry…" on a failed source: re-add it as a source + close history so the user re-scans
    /// (e.g. after removing the password or replacing the corrupt archive). (draft §10.)</summary>
    [RelayCommand]
    private void RetryFailedSource(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            AddSourcePath(path!);
        CloseHistory();
    }

    private void NotifyHistoryDetail()
    {
        OnPropertyChanged(nameof(HasHistoryDetail));
        OnPropertyChanged(nameof(SelectedHasFailedSources));
        OnPropertyChanged(nameof(SelectedHistoryTitle));
        OnPropertyChanged(nameof(SelectedHistoryCounts));
    }

    private async Task SelectHistoryRunAsync(ImportRun? run)
    {
        SelectedHistoryRun = run;
        HistoryOutcomeFilter = "all";
        HistoryOutcomeSearch = "";
        NotifyHistoryDetail();
        await HistoryOutcomesPager.LoadPageAsync(1, HistoryOutcomesPager.PageSize).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FilterHistoryOutcomesAsync(string filter)
    {
        HistoryOutcomeFilter = string.IsNullOrWhiteSpace(filter) ? "all" : filter;
        await HistoryOutcomesPager.LoadPageAsync(1, HistoryOutcomesPager.PageSize).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task HistoryPreviousPageAsync(CancellationToken cancellationToken = default) =>
        HistoryOutcomesPager.PreviousPageAsync(cancellationToken);

    [RelayCommand]
    private Task HistoryNextPageAsync(CancellationToken cancellationToken = default) =>
        HistoryOutcomesPager.NextPageAsync(cancellationToken);

    [RelayCommand]
    private Task HistoryGoToPageAsync(int pageNumber, CancellationToken cancellationToken = default) =>
        HistoryOutcomesPager.LoadPageAsync(pageNumber, HistoryOutcomesPager.PageSize, cancellationToken);

    [RelayCommand]
    private Task HistoryChangePageSizeAsync(int pageSize, CancellationToken cancellationToken = default) =>
        HistoryOutcomesPager.LoadPageAsync(1, pageSize, cancellationToken);

    partial void OnTargetRepoChanged(RepositoryInfo? value) => NotifyGate();
    partial void OnLaneFilterChanged(string value) => _ = ApplyFilterAsync();
    partial void OnSearchTextChanged(string value) => _ = ApplyFilterAsync();
    partial void OnHistoryOutcomeSearchChanged(string value) =>
        _ = HistoryOutcomesPager.LoadPageAsync(1, HistoryOutcomesPager.PageSize);

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImportItemViewModel.Decision) or nameof(ImportItemViewModel.IsResolved))
        {
            NotifyCounts();
            NotifyGate();
        }
    }

    private IEnumerable<ImportItemViewModel> CurrentFiltered()
    {
        var q = SearchText?.Trim() ?? "";
        return _all.Where(i =>
            (LaneFilter == "all" || i.Lane.ToString().Equals(LaneFilter, StringComparison.OrdinalIgnoreCase))
            && (q.Length == 0
                || i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.Creator.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task ApplyFilterAsync()
    {
        await Pager.LoadPageAsync(1, Pager.PageSize).ConfigureAwait(true);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    private async Task RefreshHistoryAsync(CancellationToken ct = default)
    {
        History.Clear();
        foreach (var run in await _import.HistoryAsync(20, ct).ConfigureAwait(true))
            History.Add(run);
    }

    private async Task<PageResult<ImportOutcomeRow>> LoadHistoryOutcomePageAsync(
        PageRequest request,
        CancellationToken cancellationToken)
    {
        if (SelectedHistoryRun is not { } run)
            return PageResult<ImportOutcomeRow>.Empty(request);

        var result = await _import.HistoryOutcomesPageAsync(
            run.Id,
            request,
            HistoryOutcomeFilter,
            HistoryOutcomeSearch,
            cancellationToken).ConfigureAwait(true);
        return new PageResult<ImportOutcomeRow>(
            result.Items.Select(o => new ImportOutcomeRow(o)).ToList(),
            result.TotalCount,
            result.PageNumber,
            result.PageSize);
    }

    private void NotifyCounts()
    {
        foreach (var n in new[] { nameof(TotalScanned), nameof(NewCount), nameof(CjkCount), nameof(ExactCount),
            nameof(NamingCount), nameof(ConflictCount), nameof(CorruptCount), nameof(ReviewTotal),
            nameof(ReviewResolved), nameof(ReviewRemaining), nameof(ReviewProgress), nameof(HasSession),
            nameof(CopyPlanned), nameof(FixPlanned), nameof(SkipPlanned), nameof(DiscardPlanned), nameof(ApplyPlanSummary),
            nameof(HasUndecidedReview) })
            OnPropertyChanged(n);
    }

    private void NotifyPendingSources()
    {
        OnPropertyChanged(nameof(HasPendingSources));
        OnPropertyChanged(nameof(HasScannedSources));
        OnPropertyChanged(nameof(SourcesSummary));
    }

    private void NotifyGate()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(GateHint));
        OnPropertyChanged(nameof(SourcesSummary));
        ScanCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        NotifyQuickImport();
        NotifyTrashOriginals();
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int pageNumber)
    {
        await Pager.LoadPageAsync(pageNumber, Pager.PageSize).ConfigureAwait(true);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int pageSize)
    {
        await Pager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    private bool CanPreviousPage() => Pager.HasPreviousPage && !Pager.IsLoading;
    private bool CanNextPage() => Pager.HasNextPage && !Pager.IsLoading;

    private Task<PageResult<ImportItemViewModel>> LoadPageAsync(PageRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var filtered = CurrentFiltered().ToList();
        var page = request.Normalize();
        return Task.FromResult(new PageResult<ImportItemViewModel>(
            filtered.Skip(page.Skip).Take(page.SafePageSize).ToList(),
            filtered.Count,
            page.SafePageNumber,
            page.SafePageSize));
    }
}
