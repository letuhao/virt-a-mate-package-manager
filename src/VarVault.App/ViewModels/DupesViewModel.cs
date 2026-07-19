using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-6 · Duplicates &amp; reclaim: exact duplicate groups. (16-checklist SCR-6.)</summary>
public sealed partial class DupesViewModel(
    IReclaimService reclaim, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Reclaim space"), new("Exact duplicates"), new("Near-duplicates"), new("Download intake")];
    [ObservableProperty] private int _selectedTabIndex;

    /// <summary>Per-group "Review" → dupe-review dialog (keep one, trash rest). (GD-10)</summary>
    [RelayCommand]
    private void Review(DuplicateGroup group)
    {
        if (group is not null)
            launcher?.OpenDupeReview(group);
    }

    public ObservableCollection<DuplicateGroup> Groups { get; } = [];

    [ObservableProperty] private long _reclaimableBytes;

    /// <summary>Reclaim summary card: number of duplicate groups. (AC-15)</summary>
    public int GroupCount => Groups.Count;
    /// <summary>Total redundant copies across all groups (each group keeps one). (AC-15)</summary>
    public int RedundantCopies => Groups.Sum(g => Math.Max(0, g.Copies.Count - 1));

    public bool IsEmpty => Groups.Count == 0;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Groups.Clear();
        long reclaim2 = 0;
        foreach (var g in await reclaim.ExactGroupsAsync(cancellationToken).ConfigureAwait(true))
        {
            Groups.Add(g);
            reclaim2 += g.Copies.Skip(1).Sum(c => c.SizeBytes);
        }
        ReclaimableBytes = reclaim2;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(GroupCount));
        OnPropertyChanged(nameof(RedundantCopies));
    }

    [RelayCommand]
    public async Task TrashGroupAsync(DuplicateGroup group, CancellationToken cancellationToken = default)
    {
        if (group.Copies.Count < 2)
            return;
        var keep = group.Copies[0].VarFileId;
        var rest = group.Copies.Skip(1).Select(c => c.VarFileId).ToList();
        await reclaim.TrashRedundantAsync(keep, rest, cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
