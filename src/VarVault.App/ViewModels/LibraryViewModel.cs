using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Settings;

namespace VarVault.App.ViewModels;

/// <summary>How the library is displayed. (1.45)</summary>
public enum LibraryViewMode { Table, Gallery }

/// <summary>The library's load state, so the view can render empty/loading/loaded/error. (1.50)</summary>
public enum LibraryState { Loading, Loaded, Empty, Error }

/// <summary>
/// Presentation logic for the library grid. A plain MVVM view-model (testable without Avalonia) that
/// binds to <see cref="ILibraryQueryService"/>: filter/search/sort/paging, view-mode toggle, selection
/// (visible vs all-matching), load states, and a remembered view persisted to settings.
/// (Checklist 1.43–1.47, 1.49, 1.50, 1.52.)
/// </summary>
public sealed partial class LibraryViewModel(
    ILibraryQueryService library,
    ISettingsService? settings = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    ILibraryActionService? actions = null,
    Services.IDialogLauncher? launcher = null,
    IPackageDetailQuery? detail = null,
    Sdk.Presets.IPresetService? presets = null,
    ITagService? tags = null,
    Domain.Indexing.IThumbnailStore? thumbnails = null,
    Services.IFileReveal? reveal = null,
    Sdk.Activation.IActivationService? activation = null,
    Sdk.Indexer.IIndexerClient? indexer = null,
    Services.EncodingFixJobRunner? encodingJobs = null) : ObservableObject, ILoadableScreen
{
    // Off-thread, cached loader for the extracted preview thumbnails the indexer stores (gallery). (1.35)
    private readonly Services.ThumbnailLoader<Avalonia.Media.Imaging.Bitmap>? _thumbLoader =
        thumbnails is null ? null : Services.ThumbnailLoader.ForBitmap(thumbnails);

    /// <summary>Right-sidebar package content gallery (wall + focus). Shared with Var detail.</summary>
    public PackageGalleryViewModel PackageGallery { get; } = new(detail, thumbnails, indexer, settings);

    /// <summary>Gallery cards (row + extracted preview thumbnail) for the gallery view. (Gallery)</summary>
    public ObservableCollection<GalleryCardViewModel> GalleryItems { get; } = [];
    /// <summary>Navigate callback set by the shell so maintenance tools can jump screens. (GD-2)</summary>
    public System.Action<string>? NavigateTo { get; set; }

    /// <summary>Raise a shell toast (with optional undo) after a real ops-bar action completes. (GF-1/AC-1/AC-3)</summary>
    public System.Action<string, System.Action?>? ShowToast { get; set; }

    /// <summary>Maintenance-tool / dependency-analysis navigation to another screen. (GD-2)</summary>
    [RelayCommand] private void Go(string screenId) => NavigateTo?.Invoke(screenId);

    /// <summary>"Detail" / "Open full detail →" → var-detail dialog for the row. (GD-4/GD-6)</summary>
    [RelayCommand]
    private void OpenDetail(PackageListEntry entry)
    {
        if (entry is not null)
            launcher?.OpenVarDetail(entry.PackageId);
    }

    /// <summary>Ops-bar "Install from txt": resolve a txt package list against the owned library. (BE-G1/AC-9)</summary>
    [RelayCommand]
    private async Task InstallFromTxtAsync(string txt, CancellationToken cancellationToken = default)
    {
        if (actions is null || string.IsNullOrWhiteSpace(txt))
            return;
        var res = await actions.ResolveTxtAsync(txt, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Matched {res.MatchedPackageIds.Count}, {res.Unmatched.Count} not owned";
    }

    /// <summary>Ops-bar "Install": add selected packages to the active loading preset and materialize links. (doc 26 · G-5)</summary>
    [RelayCommand]
    public async Task InstallSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0)
            return;
        var names = (await ResolveSelectedEntriesAsync(cancellationToken).ConfigureAwait(true))
            .Select(e => e.VarName)
            .ToList();
        var count = names.Count;
        var r = await actions.InstallIntoActiveProfileAsync(names, cancellationToken).ConfigureAwait(true);
        LastActionMessage = r.PrivilegeFailures > 0
            ? "Enable Windows Developer Mode (or run elevated) to create symlinks."
            : r.PathUnavailable > 0
                ? "VaM install path is not set or does not exist — set it in Settings before installing."
                : $"Installed {count} selected → {r.LinksCreated} linked, {r.StillMissing} missing";
        ShowToast?.Invoke(LastActionMessage, null);
        await RefreshLoadedRowsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Ops-bar "Uninstall": remove selected packages from the active loading preset and rebuild links. (doc 26 · G-5)</summary>
    [RelayCommand]
    public async Task UninstallSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0)
            return;
        var names = (await ResolveSelectedEntriesAsync(cancellationToken).ConfigureAwait(true))
            .Select(e => e.VarName)
            .ToList();
        var count = names.Count;
        var r = await actions.UninstallFromActiveProfileAsync(names, cancellationToken).ConfigureAwait(true);
        LastActionMessage = r.PrivilegeFailures > 0
            ? "Enable Windows Developer Mode (or run elevated) to create symlinks."
            : r.PathUnavailable > 0
                ? "VaM install path is not set or does not exist — set it in Settings before uninstalling."
                : $"Uninstalled {count} selected → {r.LinksRemoved} links removed";
        ShowToast?.Invoke(LastActionMessage, null);
        await RefreshLoadedRowsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Presets available as add-to-preset targets (drives the ops-bar "Add to preset…" flyout). (AC-2)</summary>
    public ObservableCollection<Sdk.Presets.PresetInfo> Presets { get; } = [];
    [ObservableProperty] private Sdk.Presets.PresetInfo? _selectedPreset;

    private async Task LoadPresetsAsync(CancellationToken cancellationToken)
    {
        if (presets is null)
            return;
        var list = await presets.ListAsync(cancellationToken).ConfigureAwait(true);
        Presets.Clear();
        foreach (var p in list)
            Presets.Add(p);
    }

    /// <summary>Resolve the selected packages to their physical var-file ids (all copies) via the detail query.</summary>
    private async Task<List<ConfirmItem>> SelectedConfirmItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<ConfirmItem>();
        if (detail is null)
            return items;
        foreach (var pkg in await ResolveSelectedEntriesAsync(cancellationToken).ConfigureAwait(true))
        {
            foreach (var copy in await GetAllCopiesAsync(pkg.PackageId, cancellationToken).ConfigureAwait(true))
                items.Add(new ConfirmItem(copy.VarFileId, pkg.VarName, pkg.IsSingleCopy));
        }
        return items;
    }

    /// <summary>Materialize selected package IDs in bounded batches (supports select-all-matching beyond the loaded page).</summary>
    private async Task<IReadOnlyList<PackageListEntry>> ResolveSelectedEntriesAsync(CancellationToken cancellationToken)
    {
        if (_selectedPackageIds.Count == 0)
            return [];
        var loaded = Items.Where(i => _selectedPackageIds.Contains(i.PackageId)).ToDictionary(i => i.PackageId);
        if (loaded.Count == _selectedPackageIds.Count)
            return _selectedPackageIds.Select(id => loaded[id]).ToList();

        var result = new List<PackageListEntry>(_selectedPackageIds.Count);
        foreach (var batch in _selectedPackageIds.Chunk(1_000))
        {
            var rows = await library.GetByIdsAsync(batch.ToList(), cancellationToken).ConfigureAwait(true);
            result.AddRange(rows);
        }
        return result;
    }

    private async Task<IReadOnlyList<CopyDto>> GetAllCopiesAsync(long packageId, CancellationToken cancellationToken)
    {
        if (detail is null)
            return [];
        var result = new List<CopyDto>();
        const int pageSize = 100;
        for (var pageNumber = 1; ; pageNumber++)
        {
            var page = await detail.GetCopiesPageAsync(
                packageId,
                new Sdk.Paging.PageRequest(pageNumber, pageSize),
                cancellationToken).ConfigureAwait(true);
            result.AddRange(page.Items);
            if (result.Count >= page.TotalCount || page.Items.Count == 0)
                return result;
        }
    }

    /// <summary>Ops-bar "Delete": open the predicate-gated confirm dialog for the selection. (AC-1/AC-4)</summary>
    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (launcher is null || SelectedCount == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var items = await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true);
        var reverseDeps = 0;
        if (detail is not null)
            foreach (var pkg in await ResolveSelectedEntriesAsync(cancellationToken).ConfigureAwait(true))
                reverseDeps += (await detail.GetOverviewAsync(pkg.PackageId, cancellationToken).ConfigureAwait(true))?.DependedOnByCount ?? 0;
        launcher.OpenConfirmDelete(items, reverseDeps);
    }

    /// <summary>Ops-bar "Fix encoding": queue mojibake fix on the selected packages' var files. (AC-3)</summary>
    [RelayCommand]
    public async Task FixEncodingSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedCount == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = (await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true)).Select(i => i.VarFileId).ToList();
        if (ids.Count == 0)
            return;

        if (encodingJobs is not null)
        {
            // Toast + Library refresh come from EncodingFixJobRunner.AfterCompleted / ShowToast.
            _ = encodingJobs.StartVarFiles(ids);
            LastActionMessage = $"Encoding fix queued for {ids.Count} files — watch the jobs panel";
            return;
        }

        if (actions is null)
            return;
        var inline = await actions.FixEncodingAsync(ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Fixed {inline.Succeeded} ({inline.Failed} skipped)";
        ShowToast?.Invoke($"Fixed encoding on {inline.Succeeded} files · originals retained", null);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sub-folder name typed into the ops-bar "Move to subfolder…" input. (AC-9)</summary>
    [ObservableProperty] private string? _subfolderName;

    /// <summary>Txt package-list pasted into the ops-bar "Install from txt" input. (AC-9)</summary>
    [ObservableProperty] private string? _installTxtInput;

    /// <summary>Ops-bar "Move to subfolder…": relocate the selection's var files within their repo. (AC-9/BE-G1)</summary>
    [RelayCommand]
    public async Task MoveToSubfolderAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0 || string.IsNullOrWhiteSpace(SubfolderName))
        {
            LastActionMessage = "Select rows and enter a sub-folder";
            return;
        }
        var ids = (await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true)).Select(i => i.VarFileId).ToList();
        var result = await actions.MoveToSubfolderAsync(ids, SubfolderName!.Trim(), cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Moved {result.Succeeded} ({result.Failed} failed)";
        ShowToast?.Invoke($"Moved {result.Succeeded} files to {SubfolderName}", null);
    }

    /// <summary>Ops-bar "Select all matching": select every package id in the current OrderedSnapshot. (1.49)</summary>
    [RelayCommand]
    public void SelectAllMatching()
    {
        _selectedPackageIds.Clear();
        if (_orderedIds.Count > 0)
        {
            foreach (var id in _orderedIds)
                _selectedPackageIds.Add(id);
        }
        else
        {
            foreach (var item in Items)
                _selectedPackageIds.Add(item.PackageId);
        }
        SyncSelectedItemsProjection();
    }

    /// <summary>Row checkbox: toggle a single row's membership in the ops selection. (AC-11)</summary>
    [RelayCommand]
    public void ToggleSelection(PackageListEntry entry)
    {
        if (entry is null)
            return;
        SetSelected(entry.PackageId, !_selectedPackageIds.Contains(entry.PackageId));
    }

    /// <summary>Whether a row is in the ops selection (drives the row checkbox state). (AC-11)</summary>
    public bool IsSelected(PackageListEntry entry) => entry is not null && _selectedPackageIds.Contains(entry.PackageId);

    /// <summary>Per-row "Fix Var" (rebuild): queue encoding fix on that package's var files. (AC-11)</summary>
    [RelayCommand]
    public async Task FixRowAsync(PackageListEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry is null || detail is null)
            return;
        var copies = await GetAllCopiesAsync(entry.PackageId, cancellationToken).ConfigureAwait(true);
        if (copies.Count == 0)
            return;
        // Skip UTF-8 siblings and any original already superseded by a sibling in this list.
        var superseded = copies
            .Where(c => c.FixedFromVarFileId.HasValue)
            .Select(c => c.FixedFromVarFileId!.Value)
            .ToHashSet();
        var ids = copies
            .Where(c => c.FixedFromVarFileId is null && !superseded.Contains(c.VarFileId))
            .Select(c => c.VarFileId)
            .ToList();
        if (ids.Count == 0)
        {
            LastActionMessage = "Nothing to fix — UTF-8 siblings already present";
            return;
        }

        if (encodingJobs is not null)
        {
            // Toast + Library refresh come from EncodingFixJobRunner.AfterCompleted / ShowToast.
            _ = encodingJobs.StartVarFiles(ids, $"Fix encoding ({entry.VarName})");
            LastActionMessage = $"Encoding fix queued for {entry.VarName} — watch the jobs panel";
            return;
        }

        if (actions is null)
            return;
        var inline = await actions.FixEncodingAsync(ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Fixed {inline.Succeeded} ({inline.Failed} skipped)";
        ShowToast?.Invoke($"Fixed {entry.VarName} · original retained", null);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Ops-bar "Add to preset…": add the selected packages to the chosen preset. (AC-2)</summary>
    [RelayCommand]
    public async Task AddToPresetAsync(long presetId, CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = _selectedPackageIds.ToList();
        var result = await actions.AddToPresetAsync(presetId, ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Added {result.Succeeded} to preset";
        ShowToast?.Invoke($"Added {result.Succeeded} packages to preset", null);
    }

    [RelayCommand]
    private Task AddToSelectedPresetAsync(CancellationToken cancellationToken = default) =>
        SelectedPreset is null
            ? Task.CompletedTask
            : AddToPresetAsync(SelectedPreset.Id, cancellationToken);
    private const int PageSize = 100;

    /// <summary>How long typing must settle before the exact faceted count is recomputed. (1.41)</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _debounceCts;
    private const string PrefSort = "library.sort.v2";
    private const string PrefDescending = "library.descending.v2";
    private const string PrefViewMode = "library.view_mode";
    private const string PrefCreator = "library.creator";
    private const string PrefTagId = "library.tag_id";
    private const string PrefDetailWidth = "library.detail_width";
    private const string PrefColumns = "library.columns.v1";
    private const string PrefFavoritesOnly = "library.favorites_only";
    private const string PrefMissingDepsOnly = "library.missing_deps_only";
    private const string PrefInstalledOnly = "library.installed_only";
    private const string PrefSingleCopyOnly = "library.single_copy_only";

    public ObservableCollection<PackageListEntry> Items { get; } = [];
    public ObservableCollection<string> Creators { get; } = [];
    /// <summary>Creators with owned-package counts for the searchable creator combo. (AC-10)</summary>
    public ObservableCollection<Controls.ComboOption> CreatorOptions { get; } = [];
    /// <summary>Canonical bulk selection by package id (covers unloaded OrderedSnapshot members).</summary>
    private readonly HashSet<long> _selectedPackageIds = [];
    private bool _projectingSelection;
    private ObservableCollection<PackageListEntry>? _selectedItems;
    /// <summary>Loaded-row projection of the ID selection for DataGrid checkbox sync.</summary>
    public ObservableCollection<PackageListEntry> SelectedItems
    {
        get
        {
            if (_selectedItems is null)
            {
                _selectedItems = [];
                _selectedItems.CollectionChanged += OnSelectedItemsMutated;
            }
            return _selectedItems;
        }
    }
    /// <summary>Total bulk-selected packages, including ones not yet loaded into <see cref="Items"/>.</summary>
    public int SelectedCount => _selectedPackageIds.Count;

    private void OnSelectedItemsMutated(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_projectingSelection)
            return;
        switch (e.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (PackageListEntry item in e.NewItems)
                    _selectedPackageIds.Add(item.PackageId);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (PackageListEntry item in e.OldItems)
                    _selectedPackageIds.Remove(item.PackageId);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                _selectedPackageIds.Clear();
                foreach (var item in SelectedItems)
                    _selectedPackageIds.Add(item.PackageId);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldItems is not null:
                foreach (PackageListEntry item in e.OldItems)
                    _selectedPackageIds.Remove(item.PackageId);
                foreach (PackageListEntry item in e.NewItems)
                    _selectedPackageIds.Add(item.PackageId);
                break;
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    /// <summary>Facet "Installed" filter — active profile members only (prototype checkbox). (AC-10)</summary>
    [ObservableProperty] private bool _installedOnly;

    /// <summary>"rows 1–N of Total" position label for the facet bar. (AC-10)</summary>
    public string PositionLabel => TotalCount == 0 ? "no rows" : $"rows 1–{Items.Count} of {TotalCount}";

    [ObservableProperty] private string? _creatorFilter;
    [ObservableProperty] private string? _packageNameFilter;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _missingDepsOnly;
    [ObservableProperty] private LibrarySort _sort = LibrarySort.Added;
    [ObservableProperty] private bool _descending = true;
    [ObservableProperty] private LibraryViewMode _viewMode = LibraryViewMode.Table;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private LibraryState _state = LibraryState.Loading;
    [ObservableProperty] private PackageListEntry? _selectedEntry;

    /// <summary>Full detail (copies + dependency closure) for the selected row, for the detail panel. (AC-13)</summary>
    [ObservableProperty] private PackageDetail? _selectedDetail;

    /// <summary>The selected package's extracted preview image — the detail hero. (doc 26 · G-2.4)</summary>
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _selectedThumbnail;
    public bool HasSelectedThumbnail => SelectedThumbnail is not null;
    partial void OnSelectedThumbnailChanged(Avalonia.Media.Imaging.Bitmap? value) => OnPropertyChanged(nameof(HasSelectedThumbnail));
    private int _detailGeneration;

    private async Task LoadSelectedDetailAsync(PackageListEntry? entry, int generation)
    {
        if (detail is null || entry is null)
        {
            if (generation == _detailGeneration)
            {
                SelectedDetail = null;
                SelectedThumbnail = null;
                PackageGallery.Clear();
            }
            return;
        }
        var detailTask = detail.GetAsync(entry.PackageId);
        await detailTask.ConfigureAwait(true);
        if (generation != _detailGeneration || SelectedEntry?.PackageId != entry.PackageId)
            return;
        SelectedDetail = await detailTask.ConfigureAwait(true);
        SelectedThumbnail = null;
        await PackageGallery.BindPackageAsync(entry.PackageId, SelectedDetail?.Copies).ConfigureAwait(true);
    }

    /// <summary>Detail-panel "resolve via alias →": open the alias dialog for the selected package. (AC-13)</summary>
    [RelayCommand]
    public void ResolveAlias()
    {
        if (SelectedEntry is not null)
            launcher?.OpenAlias(SelectedEntry.VarName);
    }

    /// <summary>Detail-panel "★ Favorite": toggle the selected package's favorite flag. (doc 26 · G-1.1)</summary>
    [RelayCommand]
    public async Task ToggleFavoriteAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedEntry is null)
            return;
        var target = SelectedEntry;
        var newValue = !target.IsFavorite;
        if (!await actions.SetFavoriteAsync(target.PackageId, newValue, cancellationToken).ConfigureAwait(true))
            return;
        // Reflect immediately: replace the (immutable) row + re-select so the grid tag and detail update.
        var updated = target with { IsFavorite = newValue };
        var index = Items.IndexOf(target);
        if (index >= 0)
            Items[index] = updated;
        SelectedEntry = updated;
    }

    /// <summary>Detail-panel "◎ Locate": reveal the selected package's file in the OS file browser. (doc 26 · G-1.2)</summary>
    [RelayCommand]
    public void Locate()
    {
        var path = SelectedDetail?.Copies.FirstOrDefault(c => c.IsOnline)?.Path
                   ?? SelectedDetail?.Copies.FirstOrDefault()?.Path;
        if (!string.IsNullOrWhiteSpace(path))
            reveal?.Reveal(path);
    }

    /// <summary>Detail-panel Edit meta… — rewrite meta.json for the selected package.</summary>
    [RelayCommand]
    public void EditMeta()
    {
        if (SelectedEntry is null)
            return;
        var entry = SelectedEntry;
        launcher?.OpenEditMeta(entry.PackageId, onSaved: () =>
        {
            _detailGeneration++;
            _ = LoadSelectedDetailAsync(entry, _detailGeneration);
        });
    }

    /// <summary>
    /// True while a keystroke is pending settle: the displayed <see cref="TotalCount"/> is the last exact
    /// value (approximate for the in-flight query) until the debounced refresh recomputes it. (1.41)
    /// </summary>
    [ObservableProperty] private bool _isCountApproximate;

    /// <summary>The debounced refresh triggered by the latest keystroke; exposed so tests can await settle. (1.41)</summary>
    public Task? PendingRefresh { get; private set; }

    public bool IsLoading => State == LibraryState.Loading;
    public bool IsEmpty => State == LibraryState.Empty;
    public bool HasError => State == LibraryState.Error;

    public bool IsTableView => ViewMode == LibraryViewMode.Table;
    public bool IsGalleryView => ViewMode == LibraryViewMode.Gallery;

    // Sortable column headers with a caret on the active sort column. (AC-11)
    private string Caret(LibrarySort col) => Sort == col ? (Descending ? " ▼" : " ▲") : string.Empty;
    public string NameHeader => "Name" + Caret(LibrarySort.Name);
    public string CreatorHeader => "Creator" + Caret(LibrarySort.Creator);
    public string SizeHeader => "Size" + Caret(LibrarySort.Size);
    public string ClassHeader => "Class" + Caret(LibrarySort.Class);
    public string AddedHeader => "Added" + Caret(LibrarySort.Added);
    public string InstalledHeader => "Installed" + Caret(LibrarySort.Installed);
    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(CreatorHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(ClassHeader));
        OnPropertyChanged(nameof(AddedHeader));
        OnPropertyChanged(nameof(InstalledHeader));
    }

    private int _loaded;
    private int _refreshGeneration;
    private IReadOnlyList<long> _orderedIds = [];
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private long? _selectedTagId;
    [ObservableProperty] private double _detailPanelWidth = 360;

    /// <summary>Whether more rows remain beyond what's loaded (drives incremental scroll load).</summary>
    public bool HasMore => _loaded < TotalCount;

    /// <summary>ILoadableScreen: the shell loads the library by refreshing it. (G-0)</summary>
    async Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken)
    {
        await LoadPreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Re-query installed-state columns for already-loaded rows without resetting scroll/selection.
    /// Call after activation updates <see cref="PackageListEntry.IsActive"/> in the read model.
    /// </summary>
    public async Task RefreshLoadedRowsAsync(CancellationToken cancellationToken = default)
    {
        if (Items.Count == 0)
            return;

        var ids = Items.Select(i => i.PackageId).ToList();
        var fresh = new Dictionary<long, PackageListEntry>(ids.Count);
        foreach (var batch in ids.Chunk(1_000))
        {
            var rows = await library.GetByIdsAsync(batch.ToList(), cancellationToken).ConfigureAwait(true);
            foreach (var row in rows)
                fresh[row.PackageId] = row;
        }

        var selectedEntryId = SelectedEntry?.PackageId;
        for (var i = 0; i < Items.Count; i++)
        {
            if (!fresh.TryGetValue(Items[i].PackageId, out var updated))
                continue;
            if (updated.IsActive == Items[i].IsActive && updated.InstalledAt == Items[i].InstalledAt)
                continue;

            Items[i] = updated;
            if (i < GalleryItems.Count)
            {
                var wasSelected = GalleryItems[i].IsSelected;
                GalleryItems[i] = new GalleryCardViewModel(updated, _thumbLoader) { IsSelected = wasSelected };
            }
        }

        if (selectedEntryId is long sid && fresh.TryGetValue(sid, out var selRow))
            SelectedEntry = selRow;

        SyncSelectedItemsProjection();
    }

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var generation = ++_refreshGeneration;
        State = LibraryState.Loading;
        NotifyStateFlags();
        try
        {
            Items.Clear();
            GalleryItems.Clear();
            _selectedPackageIds.Clear();
            SelectedItems.Clear();
            OnPropertyChanged(nameof(SelectedCount));
            SelectedEntry = null;
            SelectedDetail = null;
            SelectedThumbnail = null;
            _loaded = 0;
            _orderedIds = [];

            Creators.Clear();
            CreatorOptions.Clear();
            foreach (var creator in await library.GetCreatorsAsync(cancellationToken).ConfigureAwait(true))
                Creators.Add(creator);
            var counts = await library.GetCreatorCountsAsync(cancellationToken).ConfigureAwait(true);
            foreach (var c in counts)
                CreatorOptions.Add(new Controls.ComboOption(c.Creator, c.Count));

            await LoadPresetsAsync(cancellationToken).ConfigureAwait(true);
            await LoadTagsAsync(cancellationToken).ConfigureAwait(true);

            _orderedIds = await library.GetOrderedIdsAsync(CurrentQuery(0, 1), cancellationToken).ConfigureAwait(true);
            await LoadPageAsync(cancellationToken).ConfigureAwait(true);
            if (generation != _refreshGeneration)
                return;
            State = Items.Count == 0 ? LibraryState.Empty : LibraryState.Loaded;
            IsCountApproximate = false; // the count now reflects the settled query exactly
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            State = LibraryState.Error;
        }
        finally
        {
            NotifyStateFlags();
        }
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoadingMore)
            return;
        IsLoadingMore = true;
        var generation = _refreshGeneration;
        var skip = _loaded;
        try
        {
            var page = await LoadSnapshotSliceAsync(skip, PageSize, cancellationToken).ConfigureAwait(true);
            if (generation != _refreshGeneration)
                return; // superseded by a filter/sort refresh — discard
            foreach (var item in page.Items)
            {
                Items.Add(item);
                var card = new GalleryCardViewModel(item, _thumbLoader);
                GalleryItems.Add(card);
            }
            _loaded = skip + page.Items.Count;
            TotalCount = _orderedIds.Count > 0 ? _orderedIds.Count : page.TotalCount;
            SyncSelectedItemsProjection();
            OnPropertyChanged(nameof(HasMore));
            OnPropertyChanged(nameof(PositionLabel));
            LoadMoreCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>Bulk favorite the checked selection.</summary>
    [RelayCommand]
    public async Task FavoriteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0)
            return;
        var ids = _selectedPackageIds.ToList();
        var loadedFav = SelectedItems.Where(s => ids.Contains(s.PackageId)).ToList();
        var allFav = loadedFav.Count > 0 && loadedFav.All(s => s.IsFavorite);
        var result = await actions.SetFavoritesAsync(ids, !allFav, cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        LastActionMessage = allFav
            ? $"Unfavorited {result.Succeeded} packages"
            : $"Favorited {result.Succeeded} packages";
        ShowToast?.Invoke(LastActionMessage, null);
    }

    [RelayCommand]
    private Task FavoriteCheckedAsync(CancellationToken cancellationToken = default) =>
        SetSelectedFavoritesAsync(true, cancellationToken);

    [RelayCommand]
    private Task UnfavoriteCheckedAsync(CancellationToken cancellationToken = default) =>
        SetSelectedFavoritesAsync(false, cancellationToken);

    private async Task SetSelectedFavoritesAsync(bool favorite, CancellationToken cancellationToken)
    {
        if (actions is null || SelectedCount == 0)
            return;
        var ids = _selectedPackageIds.ToList();
        var result = await actions.SetFavoritesAsync(ids, favorite, cancellationToken).ConfigureAwait(true);
        LastActionMessage = favorite
            ? $"Favorited {result.Succeeded} packages"
            : $"Unfavorited {result.Succeeded} packages";
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
        ShowToast?.Invoke(LastActionMessage, null);
    }

    /// <summary>Gallery/table row click: set focused detail row without wiping bulk selection.</summary>
    [RelayCommand]
    public void SelectEntry(PackageListEntry? entry)
    {
        if (entry is null)
            return;
        SelectedEntry = Items.FirstOrDefault(i => i.PackageId == entry.PackageId) ?? entry;
    }

    /// <summary>Filter by tag from the rail.</summary>
    [RelayCommand]
    public async Task FilterByTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        SelectedTagId = SelectedTagId == tagId ? null : tagId;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedEntryChanged(PackageListEntry? value)
    {
        SyncGallerySelection(value?.PackageId);
        // Favorite / in-place row replace keeps the same package — don't flash-clear the gallery.
        // PackageId is set at the start of BindPackageAsync, before thumbs finish loading.
        if (value is not null && value.PackageId == PackageGallery.PackageId && PackageGallery.PackageId > 0)
            return;

        var generation = ++_detailGeneration;
        // Clear stale detail/gallery immediately so the previous package never flashes.
        SelectedDetail = null;
        SelectedThumbnail = null;
        PackageGallery.Clear();
        _ = LoadSelectedDetailAsync(value, generation);
    }

    private void SyncGallerySelection(long? packageId)
    {
        foreach (var card in GalleryItems)
            card.IsSelected = packageId is not null && card.PackageId == packageId;
    }

    /// <summary>Click a column header: toggle direction if already sorting by it, else sort by it (desc first). (1.44)</summary>
    [RelayCommand]
    public async Task SortByAsync(LibrarySort column, CancellationToken cancellationToken = default)
    {
        if (Sort == column)
            Descending = !Descending;
        else
        {
            Sort = column;
            // Newest / largest first on first click — matches the library default preference.
            Descending = true;
        }
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Toggle between the table and gallery presentations. (1.45)</summary>
    [RelayCommand]
    public async Task ToggleViewModeAsync(CancellationToken cancellationToken = default)
    {
        ViewMode = ViewMode == LibraryViewMode.Table ? LibraryViewMode.Gallery : LibraryViewMode.Table;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Select the currently-loaded (visible) rows only. (1.49)</summary>
    [RelayCommand]
    public void SelectVisible()
    {
        _selectedPackageIds.Clear();
        foreach (var item in Items)
            _selectedPackageIds.Add(item.PackageId);
        SyncSelectedItemsProjection();
    }

    public void ClearSelection()
    {
        _selectedPackageIds.Clear();
        SyncSelectedItemsProjection();
    }

    public bool SetSelected(long packageId, bool selected)
    {
        var changed = selected ? _selectedPackageIds.Add(packageId) : _selectedPackageIds.Remove(packageId);
        if (changed)
            SyncSelectedItemsProjection();
        return true;
    }

    private void SyncSelectedItemsProjection()
    {
        _projectingSelection = true;
        try
        {
            SelectedItems.Clear();
            foreach (var item in Items.Where(i => _selectedPackageIds.Contains(i.PackageId)))
                SelectedItems.Add(item);
        }
        finally
        {
            _projectingSelection = false;
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    /// <summary>Last ops-bar action result message (shown transiently). (SCR-2e)</summary>
    [ObservableProperty] private string? _lastActionMessage;

    /// <summary>Export the selected packages to a txt file via the action service. (SCR-2e / BE-N10)</summary>
    [RelayCommand]
    public async Task ExportSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedCount == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = _selectedPackageIds.ToList();
        var txt = await actions.ExportTxtAsync(ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = await Services.TxtFileIo.ExportAsync(
            SaveTxtPicker, "varvault-export.txt", txt, t => LastExportText = t, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Save-file picker hook (set by the view).</summary>
    public Func<string, Task<string?>>? SaveTxtPicker { get; set; }

    /// <summary>The most recent export text (for tests / clipboard). (SCR-2e)</summary>
    public string? LastExportText { get; private set; }

    /// <summary>Rail saved-view: toggle a favorites-only filter and refresh. (SCR-2a)</summary>
    [RelayCommand]
    public async Task ShowFavoritesAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = true;
        MissingDepsOnly = false;
        InstalledOnly = false;
        SingleCopyOnly = false;
        SelectedTagId = null;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view: clear the saved-view filters (All packages). (SCR-2a)</summary>
    [RelayCommand]
    public async Task ShowAllAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = false;
        MissingDepsOnly = false;
        InstalledOnly = false;
        SingleCopyOnly = false;
        SelectedTagId = null;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view: only rows with missing dependencies. (GD-2)</summary>
    [RelayCommand]
    public async Task ShowMissingDepsAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = false;
        MissingDepsOnly = true;
        InstalledOnly = false;
        SingleCopyOnly = false;
        SelectedTagId = null;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Facet "Single copy" filter — packages with only one physical copy (irreplaceable). (AC-12)</summary>
    [ObservableProperty] private bool _singleCopyOnly;

    /// <summary>Rail saved-view "Active in game": only packages active in the current profile. (AC-12)</summary>
    [RelayCommand]
    public async Task ShowActiveInGameAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = false; MissingDepsOnly = false; SingleCopyOnly = false; InstalledOnly = true;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view "Single copy": only irreplaceable single-copy packages. (AC-12)</summary>
    [RelayCommand]
    public async Task ShowSingleCopyAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = false; MissingDepsOnly = false; InstalledOnly = false; SingleCopyOnly = true;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Tags for the rail Tags section (name + owned count). (AC-12)</summary>
    public ObservableCollection<Sdk.Library.TagInfo> Tags { get; } = [];

    /// <summary>New-tag name typed into the rail "+ new tag" box. (AC-12)</summary>
    [ObservableProperty] private string? _newTagName;

    private async Task LoadTagsAsync(CancellationToken cancellationToken)
    {
        if (tags is null)
            return;
        var list = await tags.ListAsync(cancellationToken).ConfigureAwait(true);
        Tags.Clear();
        foreach (var t in list)
            Tags.Add(t);
        OnPropertyChanged(nameof(ActiveTagLabel));
    }

    /// <summary>Rail "+ new tag": create a tag via the tag service and refresh the section. (AC-12)</summary>
    [RelayCommand]
    public async Task CreateTagAsync(CancellationToken cancellationToken = default)
    {
        if (tags is null || string.IsNullOrWhiteSpace(NewTagName))
            return;
        await tags.CreateAsync(NewTagName!.Trim(), cancellationToken).ConfigureAwait(true);
        NewTagName = null;
        await LoadTagsAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Facet-bar "Reset": clear all filters + search back to All packages. (GD-3)</summary>
    [RelayCommand]
    public async Task ResetFiltersAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        CreatorFilter = null;
        PackageNameFilter = null;
        SearchText = null;
        FavoritesOnly = false;
        MissingDepsOnly = false;
        InstalledOnly = false;
        SingleCopyOnly = false;
        SelectedTagId = null;
        _suppressAutoRefresh = false;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sort options for the facet-bar dropdown (prototype: Recently used / Size↓ / Hot→Cold / Most depended-on). (GD-3)</summary>
    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Creator", "Size", "Class", "Added", "Installed"];

    /// <summary>Two-way selected sort label → drives <see cref="Sort"/>. (GD-3)</summary>
    public string SelectedSortLabel
    {
        get => Sort.ToString();
        set
        {
            if (Enum.TryParse<LibrarySort>(value, out var s) && s != Sort)
                _ = SortByAsync(s);
        }
    }

    /// <summary>
    /// Count of all rows matching the current filter (select-all-matching spans beyond the loaded
    /// page). (1.49)
    /// </summary>
    public async Task<int> CountAllMatchingAsync(CancellationToken cancellationToken = default)
    {
        if (_orderedIds.Count > 0)
            return _orderedIds.Count;
        var ids = await library.GetOrderedIdsAsync(CurrentQuery(0, 1), cancellationToken).ConfigureAwait(true);
        return ids.Count;
    }

    /// <summary>
    /// Ensure the OrderedSnapshot index is covered by the loaded window by appending slices (A9 windowing).
    /// </summary>
    public async Task EnsureIndexLoadedAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= TotalCount)
            return;
        while (_loaded <= index && HasMore && !cancellationToken.IsCancellationRequested)
            await LoadMoreAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Restore the remembered view (sort/direction/view-mode/creator) from settings. (1.52)</summary>
    public async Task LoadPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (settings is null)
            return;
        _suppressAutoRefresh = true;
        var sort = await settings.GetAsync(PrefSort, cancellationToken).ConfigureAwait(true);
        if (Enum.TryParse<LibrarySort>(sort, out var parsedSort))
            Sort = parsedSort;
        Descending = await settings.GetBoolAsync(PrefDescending, true, cancellationToken).ConfigureAwait(true);
        var view = await settings.GetAsync(PrefViewMode, cancellationToken).ConfigureAwait(true);
        if (Enum.TryParse<LibraryViewMode>(view, out var parsedView))
            ViewMode = parsedView;
        CreatorFilter = await settings.GetAsync(PrefCreator, cancellationToken).ConfigureAwait(true);
        var tag = await settings.GetAsync(PrefTagId, cancellationToken).ConfigureAwait(true);
        if (long.TryParse(tag, out var tagId))
            SelectedTagId = tagId;
        var width = await settings.GetAsync(PrefDetailWidth, cancellationToken).ConfigureAwait(true);
        if (double.TryParse(width, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
            DetailPanelWidth = Math.Max(PackageGalleryViewModel.MinPanelWidth, w);
        FavoritesOnly = await settings.GetBoolAsync(PrefFavoritesOnly, false, cancellationToken).ConfigureAwait(true);
        MissingDepsOnly = await settings.GetBoolAsync(PrefMissingDepsOnly, false, cancellationToken).ConfigureAwait(true);
        InstalledOnly = await settings.GetBoolAsync(PrefInstalledOnly, false, cancellationToken).ConfigureAwait(true);
        SingleCopyOnly = await settings.GetBoolAsync(PrefSingleCopyOnly, false, cancellationToken).ConfigureAwait(true);
        await PackageGallery.LoadPreferencesAsync(cancellationToken).ConfigureAwait(true);
        _suppressAutoRefresh = false;
    }

    public async Task SaveDetailWidthAsync(CancellationToken cancellationToken = default)
    {
        if (settings is null)
            return;
        DetailPanelWidth = Math.Max(PackageGalleryViewModel.MinPanelWidth, DetailPanelWidth);
        await settings.SetAsync(PrefDetailWidth, DetailPanelWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(true);
    }

    public Task<string?> LoadColumnLayoutAsync(CancellationToken cancellationToken = default) =>
        settings is null
            ? Task.FromResult<string?>(null)
            : settings.GetAsync(PrefColumns, cancellationToken);

    public Task SaveColumnLayoutAsync(string layout, CancellationToken cancellationToken = default) =>
        settings is null
            ? Task.CompletedTask
            : settings.SetAsync(PrefColumns, layout, cancellationToken);

    private async Task SavePreferencesAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
            return;
        await settings.SetAsync(PrefSort, Sort.ToString(), cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefDescending, Descending, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PrefViewMode, ViewMode.ToString(), cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PrefCreator, CreatorFilter ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PrefTagId, SelectedTagId?.ToString() ?? string.Empty, cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefFavoritesOnly, FavoritesOnly, cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefMissingDepsOnly, MissingDepsOnly, cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefInstalledOnly, InstalledOnly, cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefSingleCopyOnly, SingleCopyOnly, cancellationToken).ConfigureAwait(true);
    }

    private LibraryQuery CurrentQuery(int skip, int take) => new(
        Skip: skip,
        Take: take,
        Creator: string.IsNullOrWhiteSpace(CreatorFilter) ? null : CreatorFilter,
        SearchText: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
        FavoritesOnly: FavoritesOnly,
        MissingDepsOnly: MissingDepsOnly,
        Sort: Sort,
        Descending: Descending,
        PackageName: string.IsNullOrWhiteSpace(PackageNameFilter) ? null : PackageNameFilter,
        InstalledOnly: InstalledOnly,
        SingleCopyOnly: SingleCopyOnly,
        TagId: SelectedTagId);

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var page = await LoadSnapshotSliceAsync(_loaded, PageSize, cancellationToken).ConfigureAwait(true);
        foreach (var item in page.Items)
        {
            Items.Add(item);
            var card = new GalleryCardViewModel(item, _thumbLoader);
            GalleryItems.Add(card);
        }

        _loaded += page.Items.Count;
        TotalCount = _orderedIds.Count > 0 ? _orderedIds.Count : page.TotalCount;
        SyncSelectedItemsProjection();
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(PositionLabel));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    private async Task<LibraryPage> LoadSnapshotSliceAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        if (_orderedIds.Count == 0)
            return await library.GetPageAsync(CurrentQuery(skip, take), cancellationToken).ConfigureAwait(true);
        var ids = _orderedIds.Skip(skip).Take(take).ToList();
        var rows = await library.GetByIdsAsync(ids, cancellationToken).ConfigureAwait(true);
        if (rows.Count == 0 && ids.Count > 0)
            return await library.GetPageAsync(CurrentQuery(skip, take), cancellationToken).ConfigureAwait(true);
        return new LibraryPage(rows, _orderedIds.Count);
    }

    private void NotifyStateFlags()
    {
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    // Keep the derived state flags fresh when State changes via the generated setter.
    partial void OnStateChanged(LibraryState value) => NotifyStateFlags();

    // Typing coalesces into one exact refresh after DebounceInterval; until then the count is approximate. (1.41)
    partial void OnSearchTextChanged(string? value)
    {
        if (_suppressAutoRefresh)
            return;
        IsCountApproximate = true;
        PendingRefresh = DebouncedRefreshAsync();
    }

    private async Task DebouncedRefreshAsync()
    {
        _debounceCts?.Cancel();
        using var cts = _debounceCts = new CancellationTokenSource();
        try
        {
            await _delay(DebounceInterval, cts.Token).ConfigureAwait(true);
            await RefreshAsync(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — the newer one owns the refresh.
        }
    }

    /// <summary>Set while filters are reset/restored in bulk so per-field setters don't each trigger a refresh.</summary>
    private bool _suppressAutoRefresh;

    partial void OnInstalledOnlyChanged(bool value)
    {
        if (!_suppressAutoRefresh) _ = RefreshAsync();
    }

    partial void OnCreatorFilterChanged(string? value)
    {
        if (!_suppressAutoRefresh)
        {
            _ = SavePreferencesAsync(CancellationToken.None);
            _ = RefreshAsync();
        }
    }

    partial void OnPackageNameFilterChanged(string? value)
    {
        if (_suppressAutoRefresh)
            return;
        IsCountApproximate = true;
        PendingRefresh = DebouncedRefreshAsync();
    }

    partial void OnSelectedTagIdChanged(long? value)
    {
        OnPropertyChanged(nameof(IsTagActive));
        OnPropertyChanged(nameof(ActiveTagLabel));
        OnPropertyChanged(nameof(HasActiveTag));
    }

    public bool IsTagActive(long tagId) => SelectedTagId == tagId;
    public bool HasActiveTag => SelectedTagId is not null;
    public string ActiveTagLabel => Tags.FirstOrDefault(t => t.Id == SelectedTagId)?.Name ?? "Tag";

    [RelayCommand]
    private async Task ClearTagAsync(CancellationToken cancellationToken = default)
    {
        SelectedTagId = null;
        await SavePreferencesAsync(cancellationToken).ConfigureAwait(true);
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnSortChanged(LibrarySort value) => NotifySortHeaders();
    partial void OnDescendingChanged(bool value) => NotifySortHeaders();

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsGalleryView));
    }

    /// <summary>A tiny helper so the view can format sizes without a converter.</summary>
    public static string FormatSize(long bytes) => Common.Formatting.ByteSize.Humanize(bytes);
}
