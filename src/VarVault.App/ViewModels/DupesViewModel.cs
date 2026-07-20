using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-6 · Duplicates &amp; reclaim: exact duplicate groups. (16-checklist SCR-6.)</summary>
public sealed partial class DupesViewModel(
    IReclaimService reclaim, Services.IDialogLauncher? launcher = null, IIntakeService? intake = null) : ObservableObject, ILoadableScreen
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Reclaim space"), new("Exact duplicates"), new("Near-duplicates"), new("Download intake")];
    [ObservableProperty] private int _selectedTabIndex;

    // Per-tab visibility so switching a tab actually swaps content. (24-checklist A1/A8-A10)
    public bool IsReclaimTab => SelectedTabIndex == 0;
    public bool IsExactTab => SelectedTabIndex == 1;
    public bool IsNearTab => SelectedTabIndex == 2;
    public bool IsIntakeTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsReclaimTab));
        OnPropertyChanged(nameof(IsExactTab));
        OnPropertyChanged(nameof(IsNearTab));
        OnPropertyChanged(nameof(IsIntakeTab));
    }

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

    /// <summary>Near-duplicate groups (same payload, different identity) for the Near tab. (24-checklist A9)</summary>
    public ObservableCollection<NearDuplicateGroup> NearGroups { get; } = [];
    public bool NearIsEmpty => NearGroups.Count == 0;

    /// <summary>Download-intake tab: a folder to classify against the catalog + its results. (24-checklist A10)</summary>
    [ObservableProperty] private string? _intakeFolder;
    public ObservableCollection<IntakeItem> IntakeItems { get; } = [];
    public bool IntakeIsEmpty => IntakeItems.Count == 0;

    [RelayCommand]
    public async Task ClassifyIntakeAsync(CancellationToken cancellationToken = default)
    {
        if (intake is null || string.IsNullOrWhiteSpace(IntakeFolder))
            return;
        IntakeItems.Clear();
        foreach (var i in await intake.ClassifyFolderAsync(IntakeFolder, cancellationToken).ConfigureAwait(true))
            IntakeItems.Add(i);
        OnPropertyChanged(nameof(IntakeIsEmpty));
    }

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

        NearGroups.Clear();
        foreach (var g in await reclaim.NearDuplicateGroupsAsync(cancellationToken).ConfigureAwait(true))
            NearGroups.Add(g);
        OnPropertyChanged(nameof(NearIsEmpty));
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
