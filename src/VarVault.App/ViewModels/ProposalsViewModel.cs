using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>The kinds of reviewable proposal. (5.14)</summary>
public enum ProposalKind { Migration, Dedup, EncodingFix, Stale }

/// <summary>One pending proposal shown in the inbox.</summary>
public sealed record Proposal(long Id, ProposalKind Kind, string Description, long Bytes);

/// <summary>
/// Proposals inbox: the pending migrations/dedup/fixes/stale queue with approve/reject/batch — the
/// home of "propose, never auto-destroy". (Checklist 5.14.)
/// </summary>
public sealed partial class ProposalsViewModel : ObservableObject
{
    public ObservableCollection<Proposal> Pending { get; } = [];
    public ObservableCollection<Proposal> Approved { get; } = [];
    public ObservableCollection<Proposal> Rejected { get; } = [];

    public int PendingCount => Pending.Count;

    public void Load(IEnumerable<Proposal> proposals)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        Pending.Clear();
        Approved.Clear();
        Rejected.Clear();
        foreach (var p in proposals)
            Pending.Add(p);
        OnPropertyChanged(nameof(PendingCount));
    }

    [RelayCommand]
    public void Approve(Proposal? proposal) => Move(proposal, Approved);

    [RelayCommand]
    public void Reject(Proposal? proposal) => Move(proposal, Rejected);

    [RelayCommand]
    public void ApproveAll()
    {
        foreach (var p in Pending.ToList())
            Approved.Add(p);
        Pending.Clear();
        OnPropertyChanged(nameof(PendingCount));
    }

    private void Move(Proposal? proposal, ObservableCollection<Proposal> destination)
    {
        if (proposal is null || !Pending.Remove(proposal))
            return;
        destination.Add(proposal);
        OnPropertyChanged(nameof(PendingCount));
    }
}
