using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>A proposal row: the proposal + a checkbox + an icon glyph and tag label derived from its kind. (GD-13)</summary>
public sealed partial class ProposalRowViewModel(Proposal proposal) : ObservableObject
{
    public Proposal Proposal { get; } = proposal;
    [ObservableProperty] private bool _isSelected;
    public string Title => Proposal.Title;
    public string Detail => Proposal.Detail;
    public string Icon => Proposal.Kind switch
    {
        ProposalKind.Rebalance => "⇄",
        ProposalKind.Dedup => "⧉",
        ProposalKind.EncodingFix => "⚑",
        ProposalKind.RetireStale => "⌦",
        _ => "◈",
    };
    public string Tag => Proposal.Kind switch
    {
        ProposalKind.Dedup => "verified",
        ProposalKind.EncodingFix => "high conf.",
        _ => Common.Formatting.ByteSize.Humanize(Proposal.AffectedBytes),
    };
}

/// <summary>
/// SCR-8 · Proposals inbox: the pending migrate/dedup/encoding/stale queue with approve/reject — "propose,
/// never auto-destroy". Approve dispatches to the matching runner via <see cref="IProposalService"/>.
/// (16-checklist SCR-8.)
/// </summary>
public sealed partial class ProposalsViewModel(
    IProposalService proposals, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("All"), new("Migrations"), new("Duplicates"), new("Encoding fixes"), new("Stale")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<ProposalRowViewModel> Pending { get; } = [];

    /// <summary>Proposals filtered by the active category tab (All/Migrations/Duplicates/Encoding/Stale). (24-checklist A2)</summary>
    public IEnumerable<ProposalRowViewModel> VisibleProposals => SelectedTabIndex switch
    {
        1 => Pending.Where(p => p.Proposal.Kind == ProposalKind.Rebalance),
        2 => Pending.Where(p => p.Proposal.Kind == ProposalKind.Dedup),
        3 => Pending.Where(p => p.Proposal.Kind == ProposalKind.EncodingFix),
        4 => Pending.Where(p => p.Proposal.Kind == ProposalKind.RetireStale),
        _ => Pending,
    };

    partial void OnSelectedTabIndexChanged(int value) => OnPropertyChanged(nameof(VisibleProposals));

    [ObservableProperty] private string? _statusMessage;

    public int PendingCount => Pending.Count;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Pending.Clear();
        foreach (var p in await proposals.ListAsync(cancellationToken).ConfigureAwait(true))
            Pending.Add(new ProposalRowViewModel(p));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
    }

    /// <summary>Screen-head "Reject all" → reject every pending proposal. (GD-13)</summary>
    [RelayCommand]
    public async Task RejectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var row in Pending.ToList())
            await proposals.RejectAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        Pending.Clear();
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
    }

    /// <summary>Screen-head "Approve selected" → approve the checked proposals. (GD-13)</summary>
    [RelayCommand]
    public async Task ApproveSelectedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var row in Pending.Where(r => r.IsSelected).ToList())
        {
            var result = await proposals.ApproveAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
            StatusMessage = result.Message;
            Pending.Remove(row);
        }
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
    }

    /// <summary>Per-card "Review…" → the matching dialog for the proposal kind. (GD-13)</summary>
    [RelayCommand]
    private void Review(ProposalRowViewModel? row)
    {
        if (row is null || launcher is null)
            return;
        switch (row.Proposal.Kind)
        {
            case ProposalKind.EncodingFix: launcher.OpenFix(0, null); break;
            default: launcher.OpenMigratePlan(); break;
        }
    }

    [RelayCommand]
    public async Task ApproveAsync(ProposalRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null)
            return;
        var result = await proposals.ApproveAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.Message;
        Pending.Remove(row);
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
    }

    [RelayCommand]
    public async Task RejectAsync(ProposalRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null)
            return;
        await proposals.RejectAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        Pending.Remove(row);
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
    }
}
