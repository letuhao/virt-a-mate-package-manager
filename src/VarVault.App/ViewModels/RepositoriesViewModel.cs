using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Repositories;

namespace VarVault.App.ViewModels;

/// <summary>A repository card: the SDK info plus view-computed usage fraction + capacity label. (GD-7)</summary>
public sealed class RepositoryCardViewModel(RepositoryInfo info)
{
    public RepositoryInfo Info { get; } = info;
    public Guid Id => Info.Id;
    public string Name => Info.Name;
    public int Tier => Info.Tier;
    public string MediaType => Info.MediaType;
    public string MountPath => Info.MountPath;
    public bool IsOnline => Info.IsOnline;

    /// <summary>Used fraction 0..1 for the meter bar (0 when capacity unknown).</summary>
    public double UsedFraction => Info is { CapacityBytes: > 0 } and { FreeBytes: not null }
        ? Math.Clamp((Info.CapacityBytes.Value - Info.FreeBytes!.Value) / (double)Info.CapacityBytes.Value, 0, 1)
        : 0;

    public string CapacityLabel => Info.CapacityBytes is { } cap
        ? $"{Fmt(cap - (Info.FreeBytes ?? 0))} / {Fmt(cap)}"
        : "capacity unknown";

    private static string Fmt(long b) => b >= 1L << 40
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{b / (double)(1L << 40):F2} TB")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{b / (double)(1L << 30):F1} GB");
}

/// <summary>SCR-3 · Repositories: cards per registered repository. (16-checklist SCR-3.)</summary>
public sealed partial class RepositoriesViewModel(
    IRepositoryService repositories, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    public ObservableCollection<RepositoryCardViewModel> Repositories { get; } = [];

    public bool IsEmpty => Repositories.Count == 0;

    /// <summary>Screen-head "+ Add repository" → add-repo dialog. (GD-7)</summary>
    [RelayCommand] private void AddRepo() => launcher?.OpenAddRepo();

    /// <summary>Screen-head "Review rebalance plan" / per-card "Rebalance…" → migrate dialog. (GD-7)</summary>
    [RelayCommand] private void Rebalance() => launcher?.OpenMigratePlan();

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
