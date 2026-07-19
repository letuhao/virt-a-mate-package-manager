using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-3 · Migration plan review: copy→verify→rename→delete; single-copy excluded. (16-checklist DLG-3.)</summary>
public sealed partial class MigrateViewModel(ITieringService tiering) : ObservableObject
{
    public ObservableCollection<TierMoveProposal> Moves { get; } = [];

    [ObservableProperty] private int _excludedCount;
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Per-file safety flow, shown in the dialog (never runs unattended).</summary>
    public string Flow => "copy → verify (checksum) → atomic rename → delete source";

    public bool HasExclusions => ExcludedCount > 0;

    [RelayCommand]
    public async Task LoadPlanAsync(CancellationToken cancellationToken = default)
    {
        var plan = await tiering.BuildPlanAsync(cancellationToken).ConfigureAwait(true);
        Moves.Clear();
        foreach (var m in plan.Proposals)
            Moves.Add(m);
        ExcludedCount = plan.ExcludedCount;
        OnPropertyChanged(nameof(HasExclusions));
    }
}
