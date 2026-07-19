using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>
/// Presentation logic for the library grid. A plain MVVM view-model (testable without Avalonia) that
/// binds to <see cref="ILibraryQueryService"/> and exposes filter/sort/paging state + commands.
/// (Checklist 1.43–1.47, 1.52.)
/// </summary>
public sealed partial class LibraryViewModel(ILibraryQueryService library) : ObservableObject
{
    private const int PageSize = 100;

    public ObservableCollection<PackageListEntry> Items { get; } = [];
    public ObservableCollection<string> Creators { get; } = [];

    [ObservableProperty] private string? _creatorFilter;
    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private bool _favoritesOnly;
    [ObservableProperty] private bool _missingDepsOnly;
    [ObservableProperty] private LibrarySort _sort = LibrarySort.Name;
    [ObservableProperty] private bool _descending;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;

    private int _loaded;

    /// <summary>Whether more rows remain beyond what's loaded (drives incremental scroll load).</summary>
    public bool HasMore => _loaded < TotalCount;

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            Items.Clear();
            _loaded = 0;

            if (Creators.Count == 0)
            {
                foreach (var creator in await library.GetCreatorsAsync(cancellationToken).ConfigureAwait(true))
                    Creators.Add(creator);
            }

            await LoadPageAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    public Task LoadMoreAsync(CancellationToken cancellationToken = default) => LoadPageAsync(cancellationToken);

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        var query = new LibraryQuery(
            Skip: _loaded,
            Take: PageSize,
            Creator: string.IsNullOrWhiteSpace(CreatorFilter) ? null : CreatorFilter,
            SearchText: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            FavoritesOnly: FavoritesOnly,
            MissingDepsOnly: MissingDepsOnly,
            Sort: Sort,
            Descending: Descending);

        var page = await library.GetPageAsync(query, cancellationToken).ConfigureAwait(true);
        foreach (var item in page.Items)
            Items.Add(item);

        _loaded += page.Items.Count;
        TotalCount = page.TotalCount;
        OnPropertyChanged(nameof(HasMore));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }
}
