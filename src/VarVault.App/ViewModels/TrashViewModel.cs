using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-11 · Trash &amp; backup: restore/purge trashed items; backups. (16-checklist SCR-11.)</summary>
public sealed partial class TrashViewModel(ITrashQueryService trash) : ObservableObject, ILoadableScreen
{
    private readonly HashSet<string> _selectedIds = [];
    public PagedListState<TrashItemDto> Pager { get; } =
        new((request, ct) => trash.ListPageAsync(request, cancellationToken: ct));

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Trash (recoverable)"), new("Catalog backups")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<TrashItemDto> Items => Pager.Items;
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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TrashSummary));
    }

    [RelayCommand]
    public async Task RestoreAsync(TrashItemDto item, CancellationToken cancellationToken = default)
    {
        var r = await trash.RestoreAsync(item.Id, cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? "Restored" : r.Error.Message;
        _selectedIds.Remove(item.Id);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task PurgeAsync(TrashItemDto item, CancellationToken cancellationToken = default)
    {
        await trash.PurgeAsync(item.Id, cancellationToken).ConfigureAwait(true);
        _selectedIds.Remove(item.Id);
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
    public ObservableCollection<TrashItemDto> SelectedItems { get; } = [];
    public int SelectedCount => _selectedIds.Count;

    /// <summary>Row checkbox toggle → membership in the bulk selection. (AC-23)</summary>
    [RelayCommand]
    public void ToggleSelection(TrashItemDto item)
    {
        if (item is null) return;
        if (_selectedIds.Contains(item.Id)) _selectedIds.Remove(item.Id);
        else _selectedIds.Add(item.Id);
        SyncSelectedItems();
    }

    /// <summary>Bulk "Restore selected". (AC-23)</summary>
    [RelayCommand]
    public async Task RestoreSelectedAsync(CancellationToken cancellationToken = default)
    {
        var ids = _selectedIds.ToList();
        var ok = 0;
        foreach (var id in ids)
            if ((await trash.RestoreAsync(id, cancellationToken).ConfigureAwait(true)).IsSuccess) ok++;
        StatusMessage = $"Restored {ok} of {ids.Count}";
        _selectedIds.Clear();
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Bulk "Purge selected". (AC-23)</summary>
    [RelayCommand]
    public async Task PurgeSelectedAsync(CancellationToken cancellationToken = default)
    {
        var ids = _selectedIds.ToList();
        foreach (var id in ids)
            await trash.PurgeAsync(id, cancellationToken).ConfigureAwait(true);
        StatusMessage = $"Purged {ids.Count}";
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

    public bool IsSelected(TrashItemDto item) => _selectedIds.Contains(item.Id);

    private bool CanPreviousPage() => Pager.HasPreviousPage && !Pager.IsLoading;
    private bool CanNextPage() => Pager.HasNextPage && !Pager.IsLoading;

    private void SyncSelectedItems()
    {
        SelectedItems.Clear();
        foreach (var item in Items.Where(i => _selectedIds.Contains(i.Id)))
            SelectedItems.Add(item);
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void NotifyPager()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TrashSummary));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }
}
