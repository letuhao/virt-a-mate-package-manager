using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>
/// Reusable numbered paging state for secondary screens: one active page, exact totals, request
/// cancellation/staleness protection, and UI-friendly summary labels.
/// </summary>
public sealed partial class PagedListState<T> : ObservableObject
{
    private readonly Func<PageRequest, CancellationToken, Task<PageResult<T>>> _loader;
    private CancellationTokenSource? _activeLoad;
    private int _version;

    public PagedListState(Func<PageRequest, CancellationToken, Task<PageResult<T>>> loader, int defaultPageSize = 50)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _pageSize = NormalizePageSize(defaultPageSize);
    }

    public ObservableCollection<T> Items { get; } = [];

    [ObservableProperty] private int _pageNumber = 1;
    [ObservableProperty] private int _pageSize = 50;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    public int PageCount => TotalCount <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !IsLoading && Items.Count == 0 && string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < PageCount;
    public int FirstItemNumber => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int LastItemNumber => TotalCount == 0 ? 0 : Math.Min(TotalCount, FirstItemNumber + Items.Count - 1);
    public string SummaryLabel => TotalCount == 0 ? "0 results" : $"{FirstItemNumber}-{LastItemNumber} of {TotalCount}";

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadPageAsync(PageNumber, PageSize, cancellationToken).ConfigureAwait(true);
    }

    public async Task ResetAndReloadAsync(CancellationToken cancellationToken = default)
    {
        await LoadPageAsync(1, PageSize, cancellationToken).ConfigureAwait(true);
    }

    public async Task LoadPageAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = NormalizePageSize(pageSize);
        var request = new PageRequest(pageNumber, pageSize).Normalize();

        _activeLoad?.Cancel();
        _activeLoad?.Dispose();
        _activeLoad = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _activeLoad.Token;
        var version = ++_version;

        IsLoading = true;
        ErrorMessage = null;
        NotifyComputed();

        try
        {
            var page = await _loader(request, token).ConfigureAwait(true);
            if (version != _version)
                return;

            PageSize = NormalizePageSize(page.PageSize);
            TotalCount = Math.Max(0, page.TotalCount);
            var maxPage = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
            PageNumber = Math.Clamp(page.PageNumber, 1, Math.Max(1, maxPage));

            Items.Clear();
            foreach (var item in page.Items)
                Items.Add(item);

            // If mutation or filter shrinkage pushed us past the last page, clamp and retry once.
            if (TotalCount > 0 && request.SafePageNumber > maxPage)
                await LoadPageAsync(maxPage, PageSize, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (version != _version)
                return;
        }
        catch (Exception ex)
        {
            if (version != _version)
                return;
            Items.Clear();
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
                NotifyComputed();
            }
        }
    }

    public Task NextPageAsync(CancellationToken cancellationToken = default) =>
        HasNextPage ? LoadPageAsync(PageNumber + 1, PageSize, cancellationToken) : Task.CompletedTask;

    public Task PreviousPageAsync(CancellationToken cancellationToken = default) =>
        HasPreviousPage ? LoadPageAsync(PageNumber - 1, PageSize, cancellationToken) : Task.CompletedTask;

    partial void OnPageNumberChanged(int value) => NotifyComputed();
    partial void OnPageSizeChanged(int value) => NotifyComputed();
    partial void OnTotalCountChanged(int value) => NotifyComputed();
    partial void OnIsLoadingChanged(bool value) => NotifyComputed();
    partial void OnErrorMessageChanged(string? value) => NotifyComputed();

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasPreviousPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(FirstItemNumber));
        OnPropertyChanged(nameof(LastItemNumber));
        OnPropertyChanged(nameof(SummaryLabel));
    }

    private static int NormalizePageSize(int pageSize) => Math.Clamp(pageSize, 1, 100);
}
