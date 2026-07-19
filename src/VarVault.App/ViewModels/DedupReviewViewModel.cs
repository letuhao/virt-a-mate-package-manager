using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>One copy in a duplicate group.</summary>
public sealed record DedupCopy(long VarFileId, string RepositoryName, long Bytes, string Integrity, bool IsOnline);

/// <summary>A group of duplicate copies of one identity, with a chosen keeper.</summary>
public sealed partial class DedupGroupViewModel : ObservableObject
{
    public string Identity { get; }
    public IReadOnlyList<DedupCopy> Copies { get; }

    [ObservableProperty] private long _keeperVarFileId;

    public DedupGroupViewModel(string identity, IReadOnlyList<DedupCopy> copies)
    {
        Identity = identity;
        Copies = copies;
        // Default keeper = the healthiest online copy (integrity "Ok" ranks first).
        var keeper = copies.Where(c => c.IsOnline).OrderBy(c => c.Integrity == "Ok" ? 0 : 1).ThenBy(c => c.VarFileId).FirstOrDefault()
                     ?? copies[0];
        KeeperVarFileId = keeper.VarFileId;
    }

    /// <summary>The copies that would be removed (everything but the keeper) — verified before delete.</summary>
    public IEnumerable<DedupCopy> Removable => Copies.Where(c => c.VarFileId != KeeperVarFileId);

    public long ReclaimableBytes => Removable.Sum(c => c.Bytes);
}

/// <summary>
/// Duplicate review: keep-one per group ranked by integrity/health, verified-before-delete. (Checklist 4.9.)
/// </summary>
public sealed partial class DedupReviewViewModel : ObservableObject
{
    public ObservableCollection<DedupGroupViewModel> Groups { get; } = [];

    public long TotalReclaimableBytes => Groups.Sum(g => g.ReclaimableBytes);

    public void Load(IEnumerable<DedupGroupViewModel> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        Groups.Clear();
        foreach (var g in groups)
            Groups.Add(g);
        OnPropertyChanged(nameof(TotalReclaimableBytes));
    }

    [RelayCommand]
    public void ChooseKeeper((DedupGroupViewModel Group, long VarFileId) choice)
    {
        choice.Group.KeeperVarFileId = choice.VarFileId;
        OnPropertyChanged(nameof(TotalReclaimableBytes));
    }
}
