using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VarVault.App.ViewModels;

/// <summary>A reclaim opportunity with an estimated recoverable size.</summary>
public sealed record ReclaimItem(string Description, string Kind, long Bytes, bool IsSingleCopy);

/// <summary>
/// Reclaim wizard: aggregates duplicates + cold-on-SSD + never-loaded orphans with size estimates,
/// hard-excluding single-copy content. (Checklist 4.8.)
/// </summary>
public sealed partial class ReclaimViewModel : ObservableObject
{
    public ObservableCollection<ReclaimItem> Items { get; } = [];

    [ObservableProperty] private long _totalReclaimableBytes;

    public void Load(IEnumerable<ReclaimItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Items.Clear();
        long total = 0;
        foreach (var item in candidates)
        {
            if (item.IsSingleCopy)
                continue; // ⚠ single-copy is never a reclaim candidate
            Items.Add(item);
            total += item.Bytes;
        }
        TotalReclaimableBytes = total;
    }
}
