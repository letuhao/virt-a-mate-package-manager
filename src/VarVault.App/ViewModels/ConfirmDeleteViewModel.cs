using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>A delete candidate; single-copy items are protected (excluded from the delete). (DLG-6)</summary>
public sealed record ConfirmItem(long VarFileId, string Name, bool IsSingleCopy);

/// <summary>
/// DLG-6 · Confirm delete: single-copy items are protected/excluded; safe items go to trash via the
/// predicate-gated action service. (16-checklist DLG-6.)
/// </summary>
public sealed partial class ConfirmDeleteViewModel(ILibraryActionService actions) : ObservableObject
{
    public ObservableCollection<ConfirmItem> Items { get; } = [];

    [ObservableProperty] private string? _resultMessage;

    public IEnumerable<ConfirmItem> Deletable => Items.Where(i => !i.IsSingleCopy);
    public int ProtectedCount => Items.Count(i => i.IsSingleCopy);
    public int SafeCount => Items.Count(i => !i.IsSingleCopy);
    public bool HasProtected => ProtectedCount > 0;

    public void SetItems(IEnumerable<ConfirmItem> items)
    {
        Items.Clear();
        foreach (var i in items)
            Items.Add(i);
        OnPropertyChanged(nameof(ProtectedCount));
        OnPropertyChanged(nameof(SafeCount));
        OnPropertyChanged(nameof(HasProtected));
    }

    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        var ids = Deletable.Select(i => i.VarFileId).ToList(); // single-copy never included
        if (ids.Count == 0)
        {
            ResultMessage = "Nothing safe to delete";
            return;
        }
        var result = await actions.DeleteAsync(ids, cancellationToken).ConfigureAwait(true);
        ResultMessage = $"Moved {result.Succeeded} to trash ({result.Failed} blocked)";
    }
}
