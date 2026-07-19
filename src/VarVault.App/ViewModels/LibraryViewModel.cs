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
public sealed partial class LibraryViewModel(ILibraryQueryService library, ISettingsService? settings = null) : ObservableObject
{
    private const int PageSize = 100;
    private const string PrefSort = "library.sort";
    private const string PrefDescending = "library.descending";
    private const string PrefViewMode = "library.view_mode";
    private const string PrefCreator = "library.creator";

    public ObservableCollection<PackageListEntry> Items { get; } = [];
    public ObservableCollection<string> Creators { get; } = [];
    public ObservableCollection<PackageListEntry> SelectedItems { get; } = [];

    [ObservableProperty] private string? _creatorFilter;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _missingDepsOnly;
    [ObservableProperty] private LibrarySort _sort = LibrarySort.Name;
    [ObservableProperty] private bool _descending;
    [ObservableProperty] private LibraryViewMode _viewMode = LibraryViewMode.Table;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private LibraryState _state = LibraryState.Loading;
    [ObservableProperty] private PackageListEntry? _selectedEntry;

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
        Descending: Descending);

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
