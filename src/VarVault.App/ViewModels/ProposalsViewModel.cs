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
    IProposalService proposals,
    Services.IDialogLauncher? launcher = null,
    Services.EncodingFixJobRunner? encodingJobs = null) : ObservableObject, ILoadableScreen
{
    private readonly Dictionary<string, ProposalRowViewModel> _selectedById = [];

    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("All"), new("Migrations"), new("Duplicates"), new("Encoding fixes"), new("Stale")];
    [ObservableProperty] private int _selectedTabIndex;

    public PagedListState<ProposalRowViewModel> AllPager { get; } =
        new(async (request, ct) =>
        {
            var page = await proposals.ListPageAsync(null, request, ct).ConfigureAwait(false);
            return new VarVault.Sdk.Paging.PageResult<ProposalRowViewModel>(
                page.Items.Select(p => new ProposalRowViewModel(p)).ToList(), page.TotalCount, page.PageNumber, page.PageSize);
        });
    public PagedListState<ProposalRowViewModel> MigrationPager { get; } =
        new((request, ct) => ProjectAsync(ProposalKind.Rebalance, request, proposals, ct));
    public PagedListState<ProposalRowViewModel> DuplicatePager { get; } =
        new((request, ct) => ProjectAsync(ProposalKind.Dedup, request, proposals, ct));
    public PagedListState<ProposalRowViewModel> EncodingPager { get; } =
        new((request, ct) => ProjectAsync(ProposalKind.EncodingFix, request, proposals, ct));
    public PagedListState<ProposalRowViewModel> StalePager { get; } =
        new((request, ct) => ProjectAsync(ProposalKind.RetireStale, request, proposals, ct));

    public ObservableCollection<ProposalRowViewModel> Pending => CurrentPager.Items;
    public PagedListState<ProposalRowViewModel> CurrentPager => SelectedTabIndex switch
    {
        1 => MigrationPager,
        2 => DuplicatePager,
        3 => EncodingPager,
        4 => StalePager,
        _ => AllPager,
    };

    /// <summary>Proposals filtered by the active category tab (All/Migrations/Duplicates/Encoding/Stale). (24-checklist A2)</summary>
    public IEnumerable<ProposalRowViewModel> VisibleProposals => Pending;

    partial void OnSelectedTabIndexChanged(int value)
    {
        SyncSelection();
        OnPropertyChanged(nameof(CurrentPager));
        OnPropertyChanged(nameof(VisibleProposals));
    }

    [ObservableProperty] private string? _statusMessage;

    public int PendingCount => Pending.Count;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await AllPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await MigrationPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await DuplicatePager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await EncodingPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await StalePager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        SyncSelection();
        NotifyPager();
    }

    /// <summary>Screen-head "Reject all" → reject every pending proposal. (GD-13)</summary>
    [RelayCommand]
    public async Task RejectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var row in Pending.ToList())
            await proposals.RejectAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        _selectedById.Clear();
        Pending.Clear();
        NotifyPager();
    }

    /// <summary>Screen-head "Approve selected" → approve the checked proposals. (GD-13)</summary>
    [RelayCommand]
    public async Task ApproveSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = Pending.Where(r => r.IsSelected || _selectedById.ContainsKey(r.Proposal.Id)).ToList();
        var encoding = selected.Where(r => r.Proposal.Kind == ProposalKind.EncodingFix).ToList();
        var others = selected.Where(r => r.Proposal.Kind != ProposalKind.EncodingFix).ToList();

        if (encoding.Count > 0 && encodingJobs is not null)
        {
            var ids = encoding.SelectMany(r => r.Proposal.VarFileIds).Distinct().ToList();
            StatusMessage = "Encoding fix queued — watch the jobs panel";
            var job = encodingJobs.StartVarFiles(ids, $"Fix encoding ({ids.Count} from proposals)");
            try
            {
                var batch = await job.Result.ConfigureAwait(true);
                StatusMessage = $"Fixed {batch.Succeeded}/{ids.Count} (originals retained as .fixed.var)";
                foreach (var row in encoding)
                {
                    _selectedById.Remove(row.Proposal.Id);
                    Pending.Remove(row);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Encoding fix cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Encoding fix failed: {ex.Message}";
            }
        }
        else
        {
            others = selected; // no job runner — approve encoding via IProposalService with the rest
        }

        foreach (var row in others)
        {
            var result = await proposals.ApproveAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
            StatusMessage = result.Message;
            Pending.Remove(row);
            _selectedById.Remove(row.Proposal.Id);
        }
        NotifyPager();
    }

    /// <summary>Per-card "Review…" → the matching dialog for the proposal kind. (GD-13)</summary>
    [RelayCommand]
    private void Review(ProposalRowViewModel? row)
    {
        if (row is null || launcher is null)
            return;
        switch (row.Proposal.Kind)
        {
            case ProposalKind.EncodingFix: launcher.OpenFix(0, row.Proposal.Payload); break;
            default: launcher.OpenMigratePlan(); break;
        }
    }

    [RelayCommand]
    public async Task ApproveAsync(ProposalRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null)
            return;

        if (row.Proposal.Kind == ProposalKind.EncodingFix && encodingJobs is not null)
        {
            var ids = row.Proposal.VarFileIds;
            StatusMessage = "Encoding fix queued — watch the jobs panel";
            var job = encodingJobs.StartVarFiles(ids, "Fix encoding (proposal)");
            try
            {
                var batch = await job.Result.ConfigureAwait(true);
                StatusMessage = $"Fixed {batch.Succeeded}/{ids.Count} (originals retained as .fixed.var)";
                _selectedById.Remove(row.Proposal.Id);
                Pending.Remove(row);
                NotifyPager();
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Encoding fix cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Encoding fix failed: {ex.Message}";
            }
            return;
        }

        var result = await proposals.ApproveAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.Message;
        _selectedById.Remove(row.Proposal.Id);
        Pending.Remove(row);
        NotifyPager();
    }

    [RelayCommand]
    public async Task RejectAsync(ProposalRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row is null)
            return;
        await proposals.RejectAsync(row.Proposal, cancellationToken).ConfigureAwait(true);
        _selectedById.Remove(row.Proposal.Id);
        Pending.Remove(row);
        NotifyPager();
    }

    [RelayCommand]
    public void ToggleSelection(ProposalRowViewModel? row)
    {
        if (row is null)
            return;
        if (_selectedById.ContainsKey(row.Proposal.Id))
        {
            _selectedById.Remove(row.Proposal.Id);
            row.IsSelected = false;
        }
        else
        {
            _selectedById[row.Proposal.Id] = row;
            row.IsSelected = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await CurrentPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        SyncSelection();
        NotifyPager();
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        await CurrentPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        SyncSelection();
        NotifyPager();
    }

    [RelayCommand]
    private async Task GoToPageAsync(int pageNumber)
    {
        await CurrentPager.LoadPageAsync(pageNumber, CurrentPager.PageSize).ConfigureAwait(true);
        SyncSelection();
        NotifyPager();
    }

    [RelayCommand]
    private async Task ChangePageSizeAsync(int pageSize)
    {
        await CurrentPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        SyncSelection();
        NotifyPager();
    }

    private bool CanPreviousPage() => CurrentPager.HasPreviousPage && !CurrentPager.IsLoading;
    private bool CanNextPage() => CurrentPager.HasNextPage && !CurrentPager.IsLoading;

    private void SyncSelection()
    {
        foreach (var row in Pending)
            row.IsSelected = _selectedById.ContainsKey(row.Proposal.Id);
    }

    private void NotifyPager()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(VisibleProposals));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
    }

    private static async Task<VarVault.Sdk.Paging.PageResult<ProposalRowViewModel>> ProjectAsync(
        ProposalKind kind,
        VarVault.Sdk.Paging.PageRequest request,
        IProposalService proposals,
        CancellationToken cancellationToken)
    {
        var page = await proposals.ListPageAsync(kind, request, cancellationToken).ConfigureAwait(false);
        return new VarVault.Sdk.Paging.PageResult<ProposalRowViewModel>(
            page.Items.Select(p => new ProposalRowViewModel(p)).ToList(),
            page.TotalCount,
            page.PageNumber,
            page.PageSize);
    }
}
