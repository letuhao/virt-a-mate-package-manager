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

    /// <summary>The copy the user chose to keep; drives <see cref="KeepId"/> so the user can pick which survives. (24-checklist A11)</summary>
    [ObservableProperty] private DuplicateCopy? _selectedCopy;

    partial void OnSelectedCopyChanged(DuplicateCopy? value)
    {
        if (value is not null)
            KeepId = value.VarFileId;
    }

    public void SetGroup(DuplicateGroup group)
    {
        Copies.Clear();
        foreach (var c in group.Copies)
            Copies.Add(c);
        SelectedCopy = group.Copies.Count > 0 ? group.Copies[0] : null; // default keep = first; user can change it
        KeepId = SelectedCopy?.VarFileId ?? 0;
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
