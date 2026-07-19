using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-6 · Duplicates &amp; reclaim: exact duplicate groups. (16-checklist SCR-6.)</summary>
public sealed partial class DupesViewModel(IReclaimService reclaim) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Reclaim space"), new("Exact duplicates"), new("Near-duplicates"), new("Download intake")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<DuplicateGroup> Groups { get; } = [];

    [ObservableProperty] private long _reclaimableBytes;

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
