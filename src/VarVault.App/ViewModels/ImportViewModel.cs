using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Import;
using VarVault.Sdk.Repositories;

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
    public string MetaIdentityLabel => Model.Signals.MetaIdentity ?? "(không đọc được)";
    public bool HasExisting => Existing is not null;
    public string IntegrityStatus => Model.Signals.IntegrityStatus;
    public int EntryCount => Model.Signals.EntryCount;

    /// <summary>Decision-state pill for the list (draft): auto lanes show the lane; review lanes show decided/pending.</summary>
    public string ListPillText => NeedsReview ? (IsResolved ? "✓ " + DecisionLabel : "cần review") : LaneLabel;
    public string LaneLabel => Lane switch
    {
        ImportLane.New => "Mới", ImportLane.Exact => "Trùng", ImportLane.Cjk => "CJK",
        ImportLane.Conflict => "Xung đột", ImportLane.Naming => "Tên≠meta", _ => "Hỏng",
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
        ImportDecision.KeepIncoming => "Giữ mới", ImportDecision.KeepExisting => "Giữ cũ",
        ImportDecision.KeepBoth => "Giữ cả hai", ImportDecision.RenameToMeta => "Đổi tên theo meta",
        ImportDecision.Skip => "Bỏ qua", ImportDecision.Discard => "Loại", _ => "",
    };
}

/// <summary>
/// SCR · Import &amp; review screen (doc 30 §10). Pick source folders/archives + a target repo, scan (classify), review
/// only the conflict/naming/corrupt lanes (table or gallery + resolver), then apply. (doc 31 Phase 6.)
/// </summary>
public sealed partial class ImportViewModel(IImportService import, IRepositoryService repos)
    : ObservableObject, ILoadableScreen
{
    public static bool IsReviewLane(ImportLane l) => l is ImportLane.Conflict or ImportLane.Naming or ImportLane.Corrupt;

    private readonly List<ImportItemViewModel> _all = [];
    private ImportSession? _session;

    public ObservableCollection<RepositoryInfo> Repositories { get; } = [];
    [ObservableProperty] private RepositoryInfo? _targetRepo;

    public ObservableCollection<string> SourcePaths { get; } = [];
    public ObservableCollection<ImportSource> Sources { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];   // D1 dedup-trust warnings (offline/unindexed repo).
    public bool HasWarnings => Warnings.Count > 0;
    public ObservableCollection<ImportItemViewModel> Items { get; } = [];   // the filtered view
    public ObservableCollection<ImportRun> History { get; } = [];

    [ObservableProperty] private string _laneFilter = "all";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _galleryView;
    [ObservableProperty] private ImportItemViewModel? _selected;
    [ObservableProperty] private bool _activateAfter;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private bool _historyOpen;
    [ObservableProperty] private string? _statusMessage;

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

    /// <summary>Sources strip summary line (draft): "N folder · M archive · K lỗi/mật khẩu". </summary>
    public string SourcesSummary
    {
        get
        {
            var folders = Sources.Count(s => s.Kind == ImportSourceKind.Folder);
            var archives = Sources.Count(s => s.Kind == ImportSourceKind.Archive && s.Status == ImportSourceStatus.Ok);
            var failed = Sources.Count(s => s.Status != ImportSourceStatus.Ok);
            var s = $"{folders} folder · {archives} archive giải nén";
            return failed > 0 ? s + $" · 🔒 {failed} lỗi/mật khẩu (ghi vào History)" : s;
        }
    }

    // Live apply plan (draft action-bar note "Copy N · fix M · bỏ qua K · loại L"). (G8)
    private static readonly ImportDecision[] CopyDecisions =
        [ImportDecision.Import, ImportDecision.ImportAndFix, ImportDecision.KeepIncoming, ImportDecision.KeepBoth, ImportDecision.RenameToMeta];
    public int CopyPlanned => _all.Count(i => CopyDecisions.Contains(i.Decision));
    public int FixPlanned => _all.Count(i => i.Decision == ImportDecision.ImportAndFix
        || (i.GbkCount > 0 && (i.Decision is ImportDecision.Import or ImportDecision.KeepIncoming or ImportDecision.KeepBoth or ImportDecision.RenameToMeta)));
    public int SkipPlanned => _all.Count(i => i.Decision is ImportDecision.Skip or ImportDecision.KeepExisting or ImportDecision.None);
    public int DiscardPlanned => _all.Count(i => i.Decision == ImportDecision.Discard);
    public string ApplyPlanSummary =>
        $"Copy {CopyPlanned} · fix {FixPlanned} · bỏ qua {SkipPlanned} · loại {DiscardPlanned}";
    public bool CanApply => _session is not null && ReviewRemaining == 0 && !IsApplying && !IsScanning && _all.Count > 0;
    public bool CanScan => TargetRepo is not null && SourcePaths.Count > 0 && !IsScanning && !IsApplying;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Repositories.Clear();
        foreach (var r in await repos.ListAsync(cancellationToken).ConfigureAwait(true))
            Repositories.Add(r);
        TargetRepo ??= Repositories.FirstOrDefault(r => r.Tier == Repositories.Min(x => x.Tier)) ?? Repositories.FirstOrDefault();
        await RefreshHistoryAsync(cancellationToken).ConfigureAwait(true);
        NotifyGate();
    }

    /// <summary>"+ Folder…" — pick one or more source folders via the native OS dialog. (QoL)</summary>
    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (FolderPicker is not null)
            AddPaths(await FolderPicker().ConfigureAwait(true));
    }

    /// <summary>"+ Archive…" — pick one or more archive files (zip/7z/rar/tar) via the native OS dialog. (QoL)</summary>
    [RelayCommand]
    private async Task AddArchiveAsync()
    {
        if (ArchivePicker is not null)
            AddPaths(await ArchivePicker().ConfigureAwait(true));
    }

    private void AddPaths(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
            if (!string.IsNullOrWhiteSpace(p) && !SourcePaths.Contains(p))
                SourcePaths.Add(p);
        NotifyGate();
    }

    /// <summary>Add a source path directly (test seam / drag-drop).</summary>
    public void AddSourcePath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !SourcePaths.Contains(path))
            SourcePaths.Add(path);
        NotifyGate();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    public async Task ScanAsync()
    {
        if (TargetRepo is null || SourcePaths.Count == 0)
            return;
        IsScanning = true;
        NotifyGate();
        try
        {
            _session = await import.ScanAsync(new ImportSpec([.. SourcePaths], TargetRepo.Id, ActivateAfter)).ConfigureAwait(true);
            _all.Clear();
            foreach (var it in _session.Items)
            {
                var vm = new ImportItemViewModel(it);
                vm.PropertyChanged += OnItemChanged;
                _all.Add(vm);
            }
            Sources.Clear();
            foreach (var s in _session.Sources)
                Sources.Add(s);
            Warnings.Clear();
            foreach (var w in _session.Warnings)
                Warnings.Add(w);
            OnPropertyChanged(nameof(SourcesSummary));
            OnPropertyChanged(nameof(HasWarnings));
            ApplyFilter();
            StatusMessage = $"Đã quét {_all.Count} var · {ReviewRemaining} cần review";
        }
        catch (Exception ex)
        {
            _session = null;
            StatusMessage = $"Lỗi khi quét nguồn: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            NotifyCounts();
            NotifyGate();
        }
    }

    [RelayCommand]
    private void AcceptAll()
    {
        foreach (var i in _all.Where(x => x.NeedsReview && !x.IsResolved))
            i.Decision = i.Recommendation;
        NotifyCounts();
        NotifyGate();
    }

    /// <summary>Set one item's decision (from the resolver buttons). Param: "itemId|Decision".</summary>
    [RelayCommand]
    private void SetDecision(string param)
    {
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
        IsApplying = true;
        NotifyGate();
        try
        {
            var r = await import.ApplyAsync(_session).ConfigureAwait(true);
            StatusMessage = $"Xong: copy {r.Copied} · fix {r.Fixed} · đổi tên {r.Renamed} · bỏ qua {r.Skipped} · loại {r.Discarded}"
                            + (r.Failed > 0 ? $" · lỗi {r.Failed}" : "");
            _session = null;
            _all.Clear();
            Items.Clear();
            Sources.Clear();
            Warnings.Clear();
            OnPropertyChanged(nameof(HasWarnings));
            await RefreshHistoryAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã huỷ import — phần đã copy vẫn được giữ, phần còn lại bỏ qua (ghi vào History).";
            await RefreshHistoryAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // §11: target offline/full and other apply failures surface as a clear message, not a crash.
            StatusMessage = $"Không thể import: {ex.Message}";
        }
        finally
        {
            IsApplying = false;
            NotifyCounts();
            NotifyGate();
        }
    }

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
        if (Selected is not { NeedsReview: true } sel)
            return;
        if (pick(sel) is { } d)
            sel.Decision = d;
    }

    [RelayCommand] private void ToggleView() => GalleryView = !GalleryView;
    [RelayCommand] private void Filter(string lane) { LaneFilter = lane; ApplyFilter(); }
    [RelayCommand] private async Task OpenHistory() { await RefreshHistoryAsync().ConfigureAwait(true); HistoryOpen = true; }
    [RelayCommand] private void CloseHistory() => HistoryOpen = false;

    /// <summary>History "Retry…" on a failed source: re-add it as a source + close history so the user re-scans
    /// (e.g. after removing the password or replacing the corrupt archive). (draft §10.)</summary>
    [RelayCommand]
    private void RetryFailedSource(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            AddSourcePath(path!);
        HistoryOpen = false;
    }

    partial void OnTargetRepoChanged(RepositoryInfo? value) => NotifyGate();
    partial void OnLaneFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImportItemViewModel.Decision) or nameof(ImportItemViewModel.IsResolved))
        {
            NotifyCounts();
            NotifyGate();
        }
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? "";
        Items.Clear();
        foreach (var i in _all.Where(i =>
            (LaneFilter == "all" || i.Lane.ToString().Equals(LaneFilter, StringComparison.OrdinalIgnoreCase))
            && (q.Length == 0
                || i.FileName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || i.Creator.Contains(q, StringComparison.OrdinalIgnoreCase))))
            Items.Add(i);
        if (Selected is null || !Items.Contains(Selected))
            Selected = Items.FirstOrDefault();
    }

    private async Task RefreshHistoryAsync(CancellationToken ct = default)
    {
        History.Clear();
        foreach (var run in await import.HistoryAsync(20, ct).ConfigureAwait(true))
            History.Add(run);
    }

    private void NotifyCounts()
    {
        foreach (var n in new[] { nameof(TotalScanned), nameof(NewCount), nameof(CjkCount), nameof(ExactCount),
            nameof(NamingCount), nameof(ConflictCount), nameof(CorruptCount), nameof(ReviewTotal),
            nameof(ReviewResolved), nameof(ReviewRemaining), nameof(ReviewProgress), nameof(HasSession),
            nameof(CopyPlanned), nameof(FixPlanned), nameof(SkipPlanned), nameof(DiscardPlanned), nameof(ApplyPlanSummary) })
            OnPropertyChanged(n);
    }

    private void NotifyGate()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanScan));
        ScanCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }
}
