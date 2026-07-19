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
    ILibraryActionService? actions = null) : ObservableObject
{
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
    public ObservableCollection<PackageListEntry> SelectedItems { get; } = [];

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

    private int _loaded;

    /// <summary>Whether more rows remain beyond what's loaded (drives incremental scroll load).</summary>
    public bool HasMore => _loaded < TotalCount;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        State = LibraryState.Loading;
        NotifyStateFlags();
        try
        {
            Items.Clear();
            SelectedItems.Clear();
            _loaded = 0;

            if (Creators.Count == 0)
            {
                foreach (var creator in await library.GetCreatorsAsync(cancellationToken).ConfigureAwait(true))
                    Creators.Add(creator);
            }

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
        var sort = await settings.GetAsync(PrefSort, cancellationToken).ConfigureAwait(true);
        if (Enum.TryParse<LibrarySort>(sort, out var parsedSort))
            Sort = parsedSort;
        Descending = await settings.GetBoolAsync(PrefDescending, false, cancellationToken).ConfigureAwait(true);
        var view = await settings.GetAsync(PrefViewMode, cancellationToken).ConfigureAwait(true);
        if (Enum.TryParse<LibraryViewMode>(view, out var parsedView))
            ViewMode = parsedView;
        CreatorFilter = await settings.GetAsync(PrefCreator, cancellationToken).ConfigureAwait(true);
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
        PackageName: string.IsNullOrWhiteSpace(PackageNameFilter) ? null : PackageNameFilter);

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var page = await library.GetPageAsync(CurrentQuery(_loaded, PageSize), cancellationToken).ConfigureAwait(true);
        foreach (var item in page.Items)
            Items.Add(item);

        _loaded += page.Items.Count;
        TotalCount = page.TotalCount;
        OnPropertyChanged(nameof(HasMore));
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

    partial void OnViewModeChanged(LibraryViewMode value)
    {
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(IsGalleryView));
    }

    /// <summary>A tiny helper so the view can format sizes without a converter.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):F1} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):F1} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F0} KB"),
    };
}
