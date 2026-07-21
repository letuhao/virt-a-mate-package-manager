using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-6 · Duplicates &amp; reclaim: exact duplicate groups. (16-checklist SCR-6.)</summary>
public sealed partial class DupesViewModel(
    IReclaimService reclaim, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    public PagedListState<DuplicateGroup> ExactPager { get; } =
        new((request, ct) => reclaim.ExactGroupsPageAsync(request, ct));
    public PagedListState<NearDuplicateGroup> NearPager { get; } =
        new((request, ct) => reclaim.NearDuplicateGroupsPageAsync(request, ct));

    // NOTE: the former "Download intake" tab was retired (doc 30 §10) — the first-class Import screen replaces it
    // (extract + classify into 6 lanes + review + copy-into-repo + history). Folder classification lives there now.
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Reclaim space"), new("Exact duplicates"), new("Near-duplicates")];
    [ObservableProperty] private int _selectedTabIndex;

    // Per-tab visibility so switching a tab actually swaps content. (24-checklist A1/A8-A10)
    public bool IsReclaimTab => SelectedTabIndex == 0;
    public bool IsExactTab => SelectedTabIndex == 1;
    public bool IsNearTab => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsReclaimTab));
        OnPropertyChanged(nameof(IsExactTab));
        OnPropertyChanged(nameof(IsNearTab));
    }

    /// <summary>Per-group "Review" → dupe-review dialog (keep one, trash rest). (GD-10)</summary>
    [RelayCommand]
    private void Review(DuplicateGroup group)
    {
        if (group is not null)
            launcher?.OpenDupeReview(group);
    }

    public ObservableCollection<DuplicateGroup> Groups => ExactPager.Items;

    [ObservableProperty] private long _reclaimableBytes;

    /// <summary>Reclaim summary card: number of duplicate groups. (AC-15)</summary>
    public int GroupCount => ExactPager.TotalCount;
    /// <summary>Total redundant copies across all groups (each group keeps one). (AC-15)</summary>
    public int RedundantCopies => Groups.Sum(g => Math.Max(0, g.Copies.Count - 1));

    public bool IsEmpty => ExactPager.IsEmpty;

    /// <summary>Near-duplicate groups (same payload, different identity) for the Near tab. (24-checklist A9)</summary>
    public ObservableCollection<NearDuplicateGroup> NearGroups => NearPager.Items;
    public bool NearIsEmpty => NearPager.IsEmpty;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await ExactPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await NearPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyExact();
        NotifyNear();
    }

    [RelayCommand]
    public async Task TrashGroupAsync(DuplicateGroup group, CancellationToken cancellationToken = default)
    {
        if (group.Copies.Count < 2)
            return;
        var keep = group.Copies[0].VarFileId;
        var rest = group.Copies.Skip(1).Select(c => c.VarFileId).ToList();
        await reclaim.TrashRedundantAsync(keep, rest, cancellationToken).ConfigureAwait(true);
        await ExactPager.ReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyExact();
    }

    [RelayCommand(CanExecute = nameof(CanExactPreviousPage))]
    private async Task ExactPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await ExactPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyExact();
    }

    [RelayCommand(CanExecute = nameof(CanExactNextPage))]
    private async Task ExactNextPageAsync(CancellationToken cancellationToken = default)
    {
        await ExactPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyExact();
    }

    [RelayCommand]
    private async Task ExactGoToPageAsync(int pageNumber)
    {
        await ExactPager.LoadPageAsync(pageNumber, ExactPager.PageSize).ConfigureAwait(true);
        NotifyExact();
    }

    [RelayCommand]
    private async Task ExactChangePageSizeAsync(int pageSize)
    {
        await ExactPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyExact();
    }

    [RelayCommand(CanExecute = nameof(CanNearPreviousPage))]
    private async Task NearPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await NearPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyNear();
    }

    [RelayCommand(CanExecute = nameof(CanNearNextPage))]
    private async Task NearNextPageAsync(CancellationToken cancellationToken = default)
    {
        await NearPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyNear();
    }

    [RelayCommand]
    private async Task NearGoToPageAsync(int pageNumber)
    {
        await NearPager.LoadPageAsync(pageNumber, NearPager.PageSize).ConfigureAwait(true);
        NotifyNear();
    }

    [RelayCommand]
    private async Task NearChangePageSizeAsync(int pageSize)
    {
        await NearPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyNear();
    }

    private bool CanExactPreviousPage() => ExactPager.HasPreviousPage && !ExactPager.IsLoading;
    private bool CanExactNextPage() => ExactPager.HasNextPage && !ExactPager.IsLoading;
    private bool CanNearPreviousPage() => NearPager.HasPreviousPage && !NearPager.IsLoading;
    private bool CanNearNextPage() => NearPager.HasNextPage && !NearPager.IsLoading;

    private void NotifyExact()
    {
        ReclaimableBytes = Groups.Sum(g => g.Copies.Skip(1).Sum(c => c.SizeBytes));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(GroupCount));
        OnPropertyChanged(nameof(RedundantCopies));
        ExactPreviousPageCommand.NotifyCanExecuteChanged();
        ExactNextPageCommand.NotifyCanExecuteChanged();
    }

    private void NotifyNear()
    {
        OnPropertyChanged(nameof(NearIsEmpty));
        NearPreviousPageCommand.NotifyCanExecuteChanged();
        NearNextPageCommand.NotifyCanExecuteChanged();
    }
}
