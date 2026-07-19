using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>
/// SCR-8 · Proposals inbox: the pending migrate/dedup/encoding/stale queue with approve/reject — "propose,
/// never auto-destroy". Approve dispatches to the matching runner via <see cref="IProposalService"/>.
/// (16-checklist SCR-8.)
/// </summary>
public sealed partial class ProposalsViewModel(IProposalService proposals) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("All"), new("Migrations"), new("Duplicates"), new("Encoding fixes"), new("Stale")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<Proposal> Pending { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    public int PendingCount => Pending.Count;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Pending.Clear();
        foreach (var p in await proposals.ListAsync(cancellationToken).ConfigureAwait(true))
            Pending.Add(p);
        OnPropertyChanged(nameof(PendingCount));
    }

    [RelayCommand]
    public async Task ApproveAsync(Proposal? proposal, CancellationToken cancellationToken = default)
    {
        if (proposal is null)
            return;
        var result = await proposals.ApproveAsync(proposal, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.Message;
        Pending.Remove(proposal);
        OnPropertyChanged(nameof(PendingCount));
    }

    [RelayCommand]
    public async Task RejectAsync(Proposal? proposal, CancellationToken cancellationToken = default)
    {
        if (proposal is null)
            return;
        await proposals.RejectAsync(proposal, cancellationToken).ConfigureAwait(true);
        Pending.Remove(proposal);
        OnPropertyChanged(nameof(PendingCount));
    }
}
