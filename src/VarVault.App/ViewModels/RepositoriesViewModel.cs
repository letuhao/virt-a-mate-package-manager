using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>A repository card: the SDK info plus view-computed usage fraction + capacity label. (GD-7)</summary>
public sealed partial class RepositoryCardViewModel(RepositoryInfo info) : ObservableObject
{
    /// <summary>Inline "really remove?" confirm state (DB-only removal never deletes files). </summary>
    [ObservableProperty] private bool _isConfirmingRemove;

    public RepositoryInfo Info { get; } = info;
    public Guid Id => Info.Id;
    public string Name => Info.Name;
    public int Tier => Info.Tier;
    public string MediaType => Info.MediaType;
    public string MountPath => Info.MountPath;
    public bool IsOnline => Info.IsOnline;

    // Tier-override arguments for the "Tier ▾" menu (BE-G2 SetTier). (AC-19)
    public string TierArg1 => $"{Id}|1";
    public string TierArg2 => $"{Id}|2";
    public string TierArg3 => $"{Id}|3";

    /// <summary>Used fraction 0..1 for the meter bar (0 when capacity unknown).</summary>
    public double UsedFraction => Info is { CapacityBytes: > 0 } and { FreeBytes: not null }
        ? Math.Clamp((Info.CapacityBytes.Value - Info.FreeBytes!.Value) / (double)Info.CapacityBytes.Value, 0, 1)
        : 0;

    public string CapacityLabel => Info.CapacityBytes is { } cap
        ? $"{Fmt(cap - (Info.FreeBytes ?? 0))} / {Fmt(cap)}"
        : "capacity unknown";

    private static string Fmt(long b) => Common.Formatting.ByteSize.Humanize(b);
}

/// <summary>SCR-3 · Repositories: cards per registered repository. (16-checklist SCR-3.)</summary>
public sealed partial class RepositoriesViewModel(
    IRepositoryService repositories, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    public ObservableCollection<RepositoryCardViewModel> Repositories { get; } = [];

    public bool IsEmpty => Repositories.Count == 0;

    /// <summary>Screen-head "+ Add repository" → add-repo dialog. (GD-7)</summary>
    [RelayCommand] private void AddRepo() => launcher?.OpenAddRepo();

    /// <summary>Hook set by the shell: enqueue a background re-index of one repo, returns a job handle (or null).</summary>
    public Func<Guid, object?>? ReindexRepo { get; set; }

    /// <summary>Hook set by the shell: enqueue a background re-index of the whole library.</summary>
    public Func<object?>? ReindexAll { get; set; }

    /// <summary>Per-card "Re-index" → resume/refresh indexing of this repo as a background job. (GD-7)</summary>
    [RelayCommand]
    private void Reindex(RepositoryCardViewModel card)
    {
        if (card is null || ReindexRepo is null)
            return;
        ReindexRepo(card.Id);
        ShowToast?.Invoke($"Re-indexing “{card.Name}” — see the activity dock for progress.", null);
    }

    /// <summary>Screen-head "Re-index all" → background full re-index. (GD-7)</summary>
    [RelayCommand]
    private void ReindexAllRepos()
    {
        if (ReindexAll is null)
            return;
        ReindexAll();
        ShowToast?.Invoke("Re-indexing the library — see the activity dock for progress.", null);
    }

    /// <summary>Screen-head "Review rebalance plan" / per-card "Rebalance…" → migrate dialog. (GD-7)</summary>
    [RelayCommand] private void Rebalance() => launcher?.OpenMigratePlan();

    /// <summary>Per-card "Edit" → rename / re-point the selected repository, then refresh the list. (GD-7/AC-19)</summary>
    [RelayCommand]
    private void Edit(RepositoryCardViewModel card)
    {
        if (card is not null)
            launcher?.OpenEditRepo(card.Info, () => _ = LoadAsync());
    }

    /// <summary>Per-card "Remove" → arm the inline confirm (nothing happens yet). </summary>
    [RelayCommand]
    private void AskRemove(RepositoryCardViewModel card)
    {
        foreach (var c in Repositories)
            c.IsConfirmingRemove = ReferenceEquals(c, card); // only one card armed at a time
    }

    /// <summary>Cancel the inline remove confirm.</summary>
    [RelayCommand]
    private void CancelRemove(RepositoryCardViewModel card)
    {
        if (card is not null)
            card.IsConfirmingRemove = false;
    }

    /// <summary>Confirmed removal: de-register from the catalog only. The folder + .var files stay on disk. </summary>
    [RelayCommand]
    public async Task RemoveAsync(RepositoryCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card is null)
            return;
        var ok = await repositories.RemoveAsync(card.Id, cancellationToken).ConfigureAwait(true);
        if (ok)
        {
            ShowToast?.Invoke($"Removed “{card.Name}” from the catalog (files kept on disk).", null);
            await LoadAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Toast hook set by the shell (optional). </summary>
    public Action<string, Action?>? ShowToast { get; set; }

    /// <summary>Per-card manual tier override (BE-G2). (GD-7)</summary>
    [RelayCommand]
    public async Task SetTierAsync(string arg, CancellationToken cancellationToken = default)
    {
        // arg = "repoGuid|tier"
        var parts = arg.Split('|');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var id) || !int.TryParse(parts[1], out var tier))
            return;
        var updated = await repositories.SetTierAsync(id, tier, cancellationToken).ConfigureAwait(true);
        if (updated is not null)
            await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Repositories.Clear();
        foreach (var r in await repositories.ListAsync(cancellationToken).ConfigureAwait(true))
            Repositories.Add(new RepositoryCardViewModel(r));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    public async Task BenchmarkAsync(RepositoryCardViewModel card, CancellationToken cancellationToken = default)
    {
        if (card is null)
            return;
        var updated = await repositories.BenchmarkAsync(card.Id, cancellationToken).ConfigureAwait(true);
        if (updated is not null)
            await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
