using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;

namespace VarVault.App.ViewModels;

/// <summary>One trash row with an observable checkbox for bulk select / Select page / Select all.</summary>
public sealed partial class TrashRowViewModel(TrashItemDto item) : ObservableObject
{
    public TrashItemDto Item { get; } = item;
    public string Id => Item.Id;
    public string OriginalPath => Item.OriginalPath;
    public string Reason => Item.Reason;
    public DateTime TrashedAtUtc => Item.TrashedAtUtc;
    public long Bytes => Item.Bytes;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>SCR-11 · Trash &amp; backup: restore/purge trashed items; backups. (16-checklist SCR-11.)</summary>
public sealed partial class TrashViewModel(ITrashQueryService trash) : ObservableObject, ILoadableScreen
{
    private readonly HashSet<string> _selectedIds = new(StringComparer.Ordinal);

    public PagedListState<TrashRowViewModel> Pager { get; } =
        new(async (request, ct) =>
        {
            var page = await trash.ListPageAsync(request, cancellationToken: ct).ConfigureAwait(false);
            return new PageResult<TrashRowViewModel>(
                page.Items.Select(i => new TrashRowViewModel(i)).ToList(),
                page.TotalCount,
                page.PageNumber,
                page.PageSize);
        });

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Trash (recoverable)"), new("Catalog backups")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<TrashRowViewModel> Items => Pager.Items;
    public ObservableCollection<BackupDto> Backups { get; } = [];

    [ObservableProperty] private string? _statusMessage;
    public bool IsEmpty => Pager.IsEmpty;

    public bool IsTrashTab => SelectedTabIndex == 0;
    public bool IsBackupsTab => SelectedTabIndex == 1;
    public string TrashSummary => $"{Pager.TotalCount} items · {Backups.Count} backups";

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTrashTab));
        OnPropertyChanged(nameof(IsBackupsTab));
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await Pager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        Backups.Clear();
        foreach (var b in await trash.ListBackupsAsync(cancellationToken).ConfigureAwait(true))
            Backups.Add(b);
        // Drop selection ids that no longer exist after restore/purge.
        if (_selectedIds.Count > 0)
        {
            var stillThere = (await trash.ListAsync(cancellationToken).ConfigureAwait(true))
                .Select(i => i.Id)
                .ToHashSet(StringComparer.Ordinal);
            _selectedIds.RemoveWhere(id => !stillThere.Contains(id));
        }
        SyncSelectedItems();
        NotifyPager();
    }

    [RelayCommand]
    public async Task RestoreAsync(TrashRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null) return;
        var r = await trash.RestoreAsync(row.Id, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? "Restored" : r.Error.Message;
        if (r.IsSuccess)
        {
            _selectedIds.Remove(row.Id);
            Items.Remove(row);
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task PurgeAsync(TrashRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null) return;
        var r = await trash.PurgeAsync(row.Id, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? "Purged" : r.Error.Message;
        if (r.IsSuccess)
        {
            _selectedIds.Remove(row.Id);
            Items.Remove(row);
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var r = await trash.BackupNowAsync(cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? "Backup created" : r.Error.Message;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Rows checked for the bulk Restore/Purge actions. (AC-23)</summary>
    public ObservableCollection<TrashRowViewModel> SelectedItems { get; } = [];
    public int SelectedCount => _selectedIds.Count;

    /// <summary>Row checkbox toggle → membership in the bulk selection. (AC-23)</summary>
    [RelayCommand]
    public void ToggleSelection(TrashRowViewModel? row)
    {
        if (row is null) return;
        if (_selectedIds.Contains(row.Id))
        {
            _selectedIds.Remove(row.Id);
            row.IsSelected = false;
        }
        else
        {
            _selectedIds.Add(row.Id);
            row.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedCount));
        RebuildSelectedItems();
    }

    /// <summary>Select only the rows on the current page (safe default).</summary>
    [RelayCommand]
    public void SelectPage()
    {
        _selectedIds.Clear();
        foreach (var row in Items)
            _selectedIds.Add(row.Id);
        SyncSelectedItems();
        StatusMessage = $"Selected page ({_selectedIds.Count})";
    }

    /// <summary>Select every trash item across all pages.</summary>
    [RelayCommand]
    public async Task SelectAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await trash.ListAsync(cancellationToken).ConfigureAwait(true);
        _selectedIds.Clear();
        foreach (var item in all)
            _selectedIds.Add(item.Id);
        SyncSelectedItems();
        StatusMessage = $"Selected all {_selectedIds.Count} in trash";
    }

    /// <summary>Clear the bulk selection.</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        _selectedIds.Clear();
        SyncSelectedItems();
        StatusMessage = "Selection cleared";
    }

    /// <summary>Bulk "Restore selected" — restores only the id set, never the whole trash by implication.</summary>
    [RelayCommand]
    public async Task RestoreSelectedAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot once — do not re-query trash or expand to "all".
        var ids = _selectedIds.ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "Nothing selected";
            return;
        }

        var ok = 0;
        var fail = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await trash.RestoreAsync(id, cancellationToken).ConfigureAwait(true)).IsSuccess)
                ok++;
            else
                fail++;
        }

        StatusMessage = fail == 0
            ? $"Restored {ok} selected"
            : $"Restored {ok} of {ids.Count} selected · {fail} failed";
        _selectedIds.Clear();
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Bulk "Purge selected" — purges only the id set.</summary>
    [RelayCommand]
    public async Task PurgeSelectedAsync(CancellationToken cancellationToken = default)
    {
        var ids = _selectedIds.ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "Nothing selected";
            return;
        }

        var ok = 0;
        var fail = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((await trash.PurgeAsync(id, cancellationToken).ConfigureAwait(true)).IsSuccess)
                ok++;
            else
                fail++;
        }

        StatusMessage = fail == 0
            ? $"Purged {ok} selected"
            : $"Purged {ok} of {ids.Count} selected · {fail} failed";
        _selectedIds.Clear();
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        SyncSelectedItems();
        NotifyPager();
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await Pager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        SyncSelectedItems();
        NotifyPager();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int pageNumber)
    {
        await Pager.LoadPageAsync(pageNumber, Pager.PageSize).ConfigureAwait(true);
        SyncSelectedItems();
        NotifyPager();
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int pageSize)
    {
        await Pager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        SyncSelectedItems();
        NotifyPager();
    }

    public bool IsSelected(TrashRowViewModel row) => row is not null && _selectedIds.Contains(row.Id);

    private bool CanPreviousPage() => Pager.HasPreviousPage && !Pager.IsLoading;
    private bool CanNextPage() => Pager.HasNextPage && !Pager.IsLoading;

    private void SyncSelectedItems()
    {
        foreach (var row in Items)
            row.IsSelected = _selectedIds.Contains(row.Id);
        RebuildSelectedItems();
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void RebuildSelectedItems()
    {
        SelectedItems.Clear();
        foreach (var row in Items.Where(r => _selectedIds.Contains(r.Id)))
            SelectedItems.Add(row);
    }

    private void NotifyPager()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TrashSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
