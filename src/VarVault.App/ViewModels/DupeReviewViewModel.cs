using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-8 · Review a duplicate group: keep one, trash the rest (full-hash verified). (16-checklist DLG-8.)</summary>
public sealed partial class DupeReviewViewModel(IReclaimService reclaim) : ObservableObject
{
    public ObservableCollection<DuplicateCopy> Copies { get; } = [];

    [ObservableProperty] private long _keepId;
    [ObservableProperty] private string? _resultMessage;

    public void SetGroup(DuplicateGroup group)
    {
        Copies.Clear();
        foreach (var c in group.Copies)
            Copies.Add(c);
        KeepId = group.Copies.Count > 0 ? group.Copies[0].VarFileId : 0;
    }

    [RelayCommand]
    public async Task KeepAndTrashAsync(CancellationToken cancellationToken = default)
    {
        var rest = Copies.Where(c => c.VarFileId != KeepId).Select(c => c.VarFileId).ToList();
        if (rest.Count == 0)
            return;
        var result = await reclaim.TrashRedundantAsync(KeepId, rest, cancellationToken).ConfigureAwait(true);
        ResultMessage = $"Kept 1, trashed {result.Trashed} ({result.Blocked} blocked)";
    }
}
