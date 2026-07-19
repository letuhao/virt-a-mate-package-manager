using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-11 · Trash &amp; backup: restore/purge trashed items; backups. (16-checklist SCR-11.)</summary>
public sealed partial class TrashViewModel(ITrashQueryService trash) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Trash (recoverable)"), new("Catalog backups")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<TrashItemDto> Items { get; } = [];
    public ObservableCollection<BackupDto> Backups { get; } = [];

    [ObservableProperty] private string? _statusMessage;
    public bool IsEmpty => Items.Count == 0;

    public bool IsTrashTab => SelectedTabIndex == 0;
    public bool IsBackupsTab => SelectedTabIndex == 1;
    public string TrashSummary => $"{Items.Count} items · {Backups.Count} backups";

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTrashTab));
        OnPropertyChanged(nameof(IsBackupsTab));
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Items.Clear();
        foreach (var t in await trash.ListAsync(cancellationToken).ConfigureAwait(true))
            Items.Add(t);
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
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task PurgeAsync(TrashItemDto item, CancellationToken cancellationToken = default)
    {
        await trash.PurgeAsync(item.Id, cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var r = await trash.BackupNowAsync(cancellationToken).ConfigureAwait(true);
        StatusMessage = r.IsSuccess ? "Backup created" : r.Error.Message;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
