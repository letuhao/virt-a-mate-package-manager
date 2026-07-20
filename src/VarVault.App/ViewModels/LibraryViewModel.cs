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
    Sdk.Activation.IActivationService? activation = null) : ObservableObject, ILoadableScreen
{
    // Off-thread, cached loader for the extracted preview thumbnails the indexer stores (gallery). (1.35)
    private readonly Services.ThumbnailLoader<Avalonia.Media.Imaging.Bitmap>? _thumbLoader =
        thumbnails is null ? null : Services.ThumbnailLoader.ForBitmap(thumbnails);

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

    // ── Bulk Install / Uninstall via a dedicated "Library installs" preset (doc 26 · G-5) ────────────────
    // Reuses the proven activation path: Install = add members + build links; Uninstall = remove members + rebuild.
    private const string LibraryPresetName = "Library installs";

    private async Task<long?> EnsureLibraryPresetAsync(CancellationToken cancellationToken)
    {
        if (presets is null)
            return null;
        var existing = (await presets.ListAsync(cancellationToken).ConfigureAwait(true)).FirstOrDefault(p => p.Name == LibraryPresetName);
        if (existing is not null)
            return existing.Id;
        var created = await presets.CreateAsync(LibraryPresetName, [], cancellationToken).ConfigureAwait(true);
        return created.IsSuccess ? created.Value.Id : (long?)null;
    }

    /// <summary>Ops-bar "Install": activate the selected packages (materialize per-var symlinks). (doc 26 · G-5)</summary>
    [RelayCommand]
    public async Task InstallSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (activation is null || presets is null || SelectedItems.Count == 0)
            return;
        var presetId = await EnsureLibraryPresetAsync(cancellationToken).ConfigureAwait(true);
        if (presetId is null) { LastActionMessage = "Could not prepare the library preset."; return; }
        var count = SelectedItems.Count;
        foreach (var entry in SelectedItems.ToList())
            await presets.AddMemberAsync(presetId.Value, entry.VarName, cancellationToken).ConfigureAwait(true);
        var r = await activation.BuildProfileLinksAsync(presetId.Value, cancellationToken).ConfigureAwait(true);
        LastActionMessage = r.PrivilegeFailures > 0
            ? "Enable Windows Developer Mode (or run elevated) to create symlinks."
            : $"Installed {count} selected → {r.LinksCreated} linked, {r.MissingPackages} missing";
        ShowToast?.Invoke(LastActionMessage, null);
    }

    /// <summary>Ops-bar "Uninstall": deactivate the selected packages (remove their symlinks). (doc 26 · G-5)</summary>
    [RelayCommand]
    public async Task UninstallSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (activation is null || presets is null || SelectedItems.Count == 0)
            return;
        var presetId = await EnsureLibraryPresetAsync(cancellationToken).ConfigureAwait(true);
        if (presetId is null)
            return;
        var count = SelectedItems.Count;
        foreach (var entry in SelectedItems.ToList())
            await presets.RemoveMemberAsync(presetId.Value, entry.VarName, cancellationToken).ConfigureAwait(true);
        var r = await activation.BuildProfileLinksAsync(presetId.Value, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Uninstalled {count} selected → {r.LinksRemoved} links removed";
        ShowToast?.Invoke(LastActionMessage, null);
    }

    /// <summary>Presets available as add-to-preset targets (drives the ops-bar "Add to preset…" flyout). (AC-2)</summary>
    public ObservableCollection<Sdk.Presets.PresetInfo> Presets { get; } = [];

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
        foreach (var pkg in SelectedItems.ToList())
        {
            var d = await detail.GetAsync(pkg.PackageId, cancellationToken).ConfigureAwait(true);
            if (d is null)
                continue;
            foreach (var copy in d.Copies)
                items.Add(new ConfirmItem(copy.VarFileId, pkg.VarName, pkg.IsSingleCopy));
        }
        return items;
    }

    /// <summary>Ops-bar "Delete": open the predicate-gated confirm dialog for the selection. (AC-1/AC-4)</summary>
    [RelayCommand]
    public async Task DeleteSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (launcher is null || SelectedItems.Count == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var items = await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true);
        var reverseDeps = 0;
        if (detail is not null)
            foreach (var pkg in SelectedItems.ToList())
                reverseDeps += (await detail.GetAsync(pkg.PackageId, cancellationToken).ConfigureAwait(true))?.DependedOnByCount ?? 0;
        launcher.OpenConfirmDelete(items, reverseDeps);
    }

    /// <summary>Ops-bar "Fix encoding": fix mojibake on the selected packages' var files. (AC-3)</summary>
    [RelayCommand]
    public async Task FixEncodingSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedItems.Count == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = (await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true)).Select(i => i.VarFileId).ToList();
        if (ids.Count == 0)
            return;
        var result = await actions.FixEncodingAsync(ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Fixed {result.Succeeded} ({result.Failed} skipped)";
        ShowToast?.Invoke($"Fixed encoding on {result.Succeeded} files", null);
    }

    /// <summary>Sub-folder name typed into the ops-bar "Move to subfolder…" input. (AC-9)</summary>
    [ObservableProperty] private string? _subfolderName;

    /// <summary>Txt package-list pasted into the ops-bar "Install from txt" input. (AC-9)</summary>
    [ObservableProperty] private string? _installTxtInput;

    /// <summary>Ops-bar "Move to subfolder…": relocate the selection's var files within their repo. (AC-9/BE-G1)</summary>
    [RelayCommand]
    public async Task MoveToSubfolderAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedItems.Count == 0 || string.IsNullOrWhiteSpace(SubfolderName))
        {
            LastActionMessage = "Select rows and enter a sub-folder";
            return;
        }
        var ids = (await SelectedConfirmItemsAsync(cancellationToken).ConfigureAwait(true)).Select(i => i.VarFileId).ToList();
        var result = await actions.MoveToSubfolderAsync(ids, SubfolderName!.Trim(), cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Moved {result.Succeeded} ({result.Failed} failed)";
        ShowToast?.Invoke($"Moved {result.Succeeded} files to {SubfolderName}", null);
    }

    /// <summary>Ops-bar "select all N matching": select every currently-loaded row. (AC-9)</summary>
    [RelayCommand]
    public void SelectAllMatching() => SelectVisible();

    /// <summary>Row checkbox: toggle a single row's membership in the ops selection. (AC-11)</summary>
    [RelayCommand]
    public void ToggleSelection(PackageListEntry entry)
    {
        if (entry is null)
            return;
        if (SelectedItems.Contains(entry))
            SelectedItems.Remove(entry);
        else
            SelectedItems.Add(entry);
    }

    /// <summary>Whether a row is in the ops selection (drives the row checkbox state). (AC-11)</summary>
    public bool IsSelected(PackageListEntry entry) => SelectedItems.Contains(entry);

    /// <summary>Per-row "Fix Var" (rebuild): fix encoding on that package's var files. (AC-11)</summary>
    [RelayCommand]
    public async Task FixRowAsync(PackageListEntry entry, CancellationToken cancellationToken = default)
    {
        if (actions is null || entry is null || detail is null)
            return;
        var d = await detail.GetAsync(entry.PackageId, cancellationToken).ConfigureAwait(true);
        if (d is null)
            return;
        var result = await actions.FixEncodingAsync(d.Copies.Select(c => c.VarFileId).ToList(), cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Fixed {result.Succeeded} ({result.Failed} skipped)";
        ShowToast?.Invoke($"Fixed {entry.VarName}", null);
    }

    /// <summary>Ops-bar "Add to preset…": add the selected packages to the chosen preset. (AC-2)</summary>
    [RelayCommand]
    public async Task AddToPresetAsync(long presetId, CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedItems.Count == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = SelectedItems.Select(s => s.PackageId).ToList();
        var result = await actions.AddToPresetAsync(presetId, ids, cancellationToken).ConfigureAwait(true);
        LastActionMessage = $"Added {result.Succeeded} to preset";
        ShowToast?.Invoke($"Added {result.Succeeded} packages to preset", null);
    }
    private const int PageSize = 100;

    /// <summary>How long typing must settle before the exact faceted count is recomputed. (1.41)</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private CancellationTokenSource? _debounceCts;
    private const string PrefSort = "library.sort";
    private const string PrefDescending = "library.descending";
    private const string PrefViewMode = "library.view_mode";
    private const string PrefCreator = "library.creator";

    public ObservableCollection<PackageListEntry> Items { get; } = [];
    public ObservableCollection<string> Creators { get; } = [];
    /// <summary>Creators with owned-package counts for the searchable creator combo. (AC-10)</summary>
    public ObservableCollection<Controls.ComboOption> CreatorOptions { get; } = [];
    public ObservableCollection<PackageListEntry> SelectedItems { get; } = [];

    /// <summary>Facet "Installed" filter — active profile members only (prototype checkbox). (AC-10)</summary>
    [ObservableProperty] private bool _installedOnly;

    /// <summary>"rows 1–N of Total" position label for the facet bar. (AC-10)</summary>
    public string PositionLabel => TotalCount == 0 ? "no rows" : $"rows 1–{Items.Count} of {TotalCount}";

    [ObservableProperty] private string? _creatorFilter;
    [ObservableProperty] private string? _packageNameFilter;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _missingDepsOnly;
    [ObservableProperty] private LibrarySort _sort = LibrarySort.Name;
    [ObservableProperty] private bool _descending;
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

    partial void OnSelectedEntryChanged(PackageListEntry? value) => _ = LoadSelectedDetailAsync(value);

    private async Task LoadSelectedDetailAsync(PackageListEntry? entry)
    {
        if (detail is null || entry is null)
        {
            SelectedDetail = null;
            SelectedThumbnail = null;
            return;
        }
        SelectedDetail = await detail.GetAsync(entry.PackageId).ConfigureAwait(true);
        // G-2.4 · load the extracted preview for the detail hero (per-package thumbnail store).
        SelectedThumbnail = _thumbLoader is null ? null : await _thumbLoader.LoadAsync(entry.PackageId).ConfigureAwait(true);
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
    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameHeader));
        OnPropertyChanged(nameof(CreatorHeader));
        OnPropertyChanged(nameof(SizeHeader));
        OnPropertyChanged(nameof(ClassHeader));
    }

    private int _loaded;

    /// <summary>Whether more rows remain beyond what's loaded (drives incremental scroll load).</summary>
    public bool HasMore => _loaded < TotalCount;

    /// <summary>ILoadableScreen: the shell loads the library by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        State = LibraryState.Loading;
        NotifyStateFlags();
        try
        {
            Items.Clear();
            GalleryItems.Clear();
            SelectedItems.Clear();
            _loaded = 0;

            if (Creators.Count == 0)
            {
                foreach (var creator in await library.GetCreatorsAsync(cancellationToken).ConfigureAwait(true))
                    Creators.Add(creator);
                var counts = await library.GetCreatorCountsAsync(cancellationToken).ConfigureAwait(true);
                foreach (var c in counts)
                    CreatorOptions.Add(new Controls.ComboOption(c.Creator, c.Count));
            }

            if (Presets.Count == 0)
                await LoadPresetsAsync(cancellationToken).ConfigureAwait(true);

            if (Tags.Count == 0)
                await LoadTagsAsync(cancellationToken).ConfigureAwait(true);

            await LoadPageAsync(cancellationToken).ConfigureAwait(true);
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
    public Task LoadMoreAsync(CancellationToken cancellationToken = default) => LoadPageAsync(cancellationToken);

    /// <summary>Click a column header: toggle direction if already sorting by it, else sort by it ascending. (1.44)</summary>
    [RelayCommand]
    public async Task SortByAsync(LibrarySort column, CancellationToken cancellationToken = default)
    {
        if (Sort == column)
            Descending = !Descending;
        else
        {
            Sort = column;
            Descending = false;
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

    /// <summary>Select the currently-loaded (visible) rows. (1.49)</summary>
    [RelayCommand]
    public void SelectVisible()
    {
        SelectedItems.Clear();
        foreach (var item in Items)
            SelectedItems.Add(item);
    }

    public void ClearSelection() => SelectedItems.Clear();

    /// <summary>Last ops-bar action result message (shown transiently). (SCR-2e)</summary>
    [ObservableProperty] private string? _lastActionMessage;

    /// <summary>Export the selected packages to a txt list via the action service. (SCR-2e / BE-N10)</summary>
    [RelayCommand]
    public async Task ExportSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (actions is null || SelectedItems.Count == 0)
        {
            LastActionMessage = "Nothing selected";
            return;
        }
        var ids = SelectedItems.Select(s => s.PackageId).ToList();
        var txt = await actions.ExportTxtAsync(ids, cancellationToken).ConfigureAwait(true);
        LastExportText = txt;
        LastActionMessage = $"Exported {ids.Count} packages";
    }

    /// <summary>The most recent export text (for save-to-file by the view). (SCR-2e)</summary>
    public string? LastExportText { get; private set; }

    /// <summary>Rail saved-view: toggle a favorites-only filter and refresh. (SCR-2a)</summary>
    [RelayCommand]
    public async Task ShowFavoritesAsync(CancellationToken cancellationToken = default)
    {
        FavoritesOnly = true;
        MissingDepsOnly = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view: clear the saved-view filters (All packages). (SCR-2a)</summary>
    [RelayCommand]
    public async Task ShowAllAsync(CancellationToken cancellationToken = default)
    {
        FavoritesOnly = false;
        MissingDepsOnly = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view: only rows with missing dependencies. (GD-2)</summary>
    [RelayCommand]
    public async Task ShowMissingDepsAsync(CancellationToken cancellationToken = default)
    {
        FavoritesOnly = false;
        MissingDepsOnly = true;
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
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rail saved-view "Single copy": only irreplaceable single-copy packages. (AC-12)</summary>
    [RelayCommand]
    public async Task ShowSingleCopyAsync(CancellationToken cancellationToken = default)
    {
        _suppressAutoRefresh = true;
        FavoritesOnly = false; MissingDepsOnly = false; InstalledOnly = false; SingleCopyOnly = true;
        _suppressAutoRefresh = false;
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
        _suppressAutoRefresh = false;
        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sort options for the facet-bar dropdown (prototype: Recently used / Size↓ / Hot→Cold / Most depended-on). (GD-3)</summary>
    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Creator", "Size", "Class"];

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
        var ids = await library.GetOrderedIdsAsync(CurrentQuery(0, 1), cancellationToken).ConfigureAwait(true);
        return ids.Count;
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
        Descending = await settings.GetBoolAsync(PrefDescending, false, cancellationToken).ConfigureAwait(true);
        var view = await settings.GetAsync(PrefViewMode, cancellationToken).ConfigureAwait(true);
        if (Enum.TryParse<LibraryViewMode>(view, out var parsedView))
            ViewMode = parsedView;
        CreatorFilter = await settings.GetAsync(PrefCreator, cancellationToken).ConfigureAwait(true);
        _suppressAutoRefresh = false;
    }

    private async Task SavePreferencesAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
            return;
        await settings.SetAsync(PrefSort, Sort.ToString(), cancellationToken).ConfigureAwait(true);
        await settings.SetBoolAsync(PrefDescending, Descending, cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PrefViewMode, ViewMode.ToString(), cancellationToken).ConfigureAwait(true);
        await settings.SetAsync(PrefCreator, CreatorFilter ?? string.Empty, cancellationToken).ConfigureAwait(true);
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
        SingleCopyOnly: SingleCopyOnly);

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var page = await library.GetPageAsync(CurrentQuery(_loaded, PageSize), cancellationToken).ConfigureAwait(true);
        foreach (var item in page.Items)
        {
            Items.Add(item);
            var card = new GalleryCardViewModel(item, _thumbLoader);
            GalleryItems.Add(card);
            _ = card.LoadAsync(); // extract/decode the preview off the UI thread; placeholder until it lands
        }

        _loaded += page.Items.Count;
        TotalCount = page.TotalCount;
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(PositionLabel));
        LoadMoreCommand.NotifyCanExecuteChanged();
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
        if (!_suppressAutoRefresh) _ = RefreshAsync();
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
