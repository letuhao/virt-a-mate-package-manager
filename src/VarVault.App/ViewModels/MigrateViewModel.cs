using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>DLG-3 · Migration plan review: copy→verify→rename→delete; single-copy excluded. Dry-run shows the
/// propose-only plan; Approve executes it through the durable migration service. (16-checklist DLG-3 / AC-5.)</summary>
public sealed partial class MigrateViewModel(ITieringService tiering, IMigrationService? migration = null,
    IRepositoryService? repositories = null) : ObservableObject
{
    public ObservableCollection<TierMoveProposal> Moves { get; } = [];

    [ObservableProperty] private int _excludedCount;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isDryRun = true;

    /// <summary>Per-file safety flow, shown in the dialog (never runs unattended).</summary>
    public string Flow => "copy → verify (checksum) → atomic rename → delete source";

    public bool HasExclusions => ExcludedCount > 0;
    public bool CanApprove => migration is not null && Moves.Count > 0;

    [RelayCommand]
    public async Task LoadPlanAsync(CancellationToken cancellationToken = default)
    {
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(true);
        Moves.Clear();
        foreach (var m in plan.Proposals)
            Moves.Add(m);
        ExcludedCount = plan.ExcludedCount;
        OnPropertyChanged(nameof(HasExclusions));
        OnPropertyChanged(nameof(CanApprove));
    }

    /// <summary>"Dry run": (re)load the propose-only plan without moving anything. (AC-5)</summary>
    [RelayCommand]
    public async Task DryRunAsync(CancellationToken cancellationToken = default)
    {
        IsDryRun = true;
        await LoadPlanAsync(cancellationToken).ConfigureAwait(true);
        StatusMessage = $"Dry run: {Moves.Count} move(s) proposed, {ExcludedCount} excluded — nothing moved.";
    }

    /// <summary>"Approve &amp; run": execute the plan through the durable migration service. (AC-5)</summary>
    [RelayCommand]
    public async Task ApproveAndRunAsync(CancellationToken cancellationToken = default)
    {
        if (migration is null || repositories is null)
        {
            StatusMessage = "Migration is unavailable.";
            return;
        }
        if (Moves.Count == 0)
        {
            StatusMessage = "Nothing to migrate.";
            return;
        }

        // Map each proposal's destination tier to a concrete online target repository.
        var repos = await repositories.ListAsync(cancellationToken).ConfigureAwait(true);
        var targetByTier = repos
            .Where(r => r.IsOnline && r.IsEnabled)
            .GroupBy(r => r.Tier)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var requests = new List<MigrationRequest>();
        foreach (var move in Moves)
            if (targetByTier.TryGetValue(move.ToTier, out var repoId))
                requests.Add(new MigrationRequest(move.VarFileId, repoId));

        if (requests.Count == 0)
        {
            StatusMessage = "No online target repository for the destination tier(s).";
            return;
        }

        IsDryRun = false;
        var result = await migration.RunAsync(requests, cancellationToken).ConfigureAwait(true);
        StatusMessage = $"Migrated {result.Moved} file(s), {result.Failed} failed.";
    }
}
