using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>Activity history screen: recent audited actions, newest first, numbered pages. (Checklist X.12.)</summary>
public sealed partial class ActivityViewModel : ObservableObject, ILoadableScreen
{
    private readonly IActivityLog _log;

    public ActivityViewModel(IActivityLog log)
    {
        _log = log;
        Pager = new PagedListState<ActivityRecord>(LoadPageAsync);
    }

    public PagedListState<ActivityRecord> Pager { get; }

    public ObservableCollection<ActivityRecord> Items => Pager.Items;

    /// <summary>Action filter options (prototype dropdown). (GD-16)</summary>
    public IReadOnlyList<string> Filters { get; } = ["All actions", "Migrations", "Deletes", "Fixes"];
    [ObservableProperty] private string _selectedFilter = "All actions";

    public bool IsEmpty => Pager.IsEmpty;

    /// <summary>ILoadableScreen: the shell loads this screen by refreshing it. (G-0)</summary>
    Task ILoadableScreen.LoadAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Pager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    partial void OnSelectedFilterChanged(string value) => _ = RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int pageNumber)
    {
        await Pager.LoadPageAsync(pageNumber, Pager.PageSize).ConfigureAwait(true);
        NotifyPager();
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int pageSize)
    {
        await Pager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPager();
    }

    private bool CanPreviousPage() => Pager.HasPreviousPage && !Pager.IsLoading;
    private bool CanNextPage() => Pager.HasNextPage && !Pager.IsLoading;

    private void NotifyPager()
    {
        OnPropertyChanged(nameof(IsEmpty));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private async Task<PageResult<ActivityRecord>> LoadPageAsync(PageRequest request, CancellationToken ct)
    {
        var page = request.Normalize();
        if (SelectedFilter == "All actions")
            return await _log.GetRecentPageAsync(page, ct).ConfigureAwait(false);

        // Kind filters are rare and low-cardinality relative to "All"; project from a capped recent window.
        var recent = await _log.GetRecentAsync(limit: 1000, cancellationToken: ct).ConfigureAwait(false);
        var filtered = recent.Where(Matches).ToList();
        return new PageResult<ActivityRecord>(
            filtered.Skip(page.Skip).Take(page.SafePageSize).ToList(),
            filtered.Count,
            page.SafePageNumber,
            page.SafePageSize);
    }

    private bool Matches(ActivityRecord r) => SelectedFilter switch
    {
        "Migrations" => r.Kind.Contains("migrat", StringComparison.OrdinalIgnoreCase),
        "Deletes" => r.Kind.Contains("delet", StringComparison.OrdinalIgnoreCase) || r.Kind.Contains("trash", StringComparison.OrdinalIgnoreCase),
        "Fixes" => r.Kind.Contains("fix", StringComparison.OrdinalIgnoreCase),
        _ => true,
    };
}
