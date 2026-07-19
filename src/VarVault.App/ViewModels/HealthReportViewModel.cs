using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VarVault.App.ViewModels;

/// <summary>A broken-encoding var eligible for fixing.</summary>
public sealed record HealthEntry(long VarFileId, string VarName, string Codepage, int BrokenEntryCount);

/// <summary>Vars sharing a detected codepage, with a batch fix-all.</summary>
public sealed partial class HealthGroupViewModel(string codepage, IReadOnlyList<HealthEntry> entries) : ObservableObject
{
    public string Codepage { get; } = codepage;
    public IReadOnlyList<HealthEntry> Entries { get; } = entries;
    public int Count => Entries.Count;
}

/// <summary>
/// Encoding health report grouped by detected codepage, with a "fix all" per group. (Checklist 4.16.)
/// </summary>
public sealed partial class HealthReportViewModel(Func<IReadOnlyList<HealthEntry>, Task>? fixBatch = null) : ObservableObject
{
    public ObservableCollection<HealthGroupViewModel> Groups { get; } = [];

    public int TotalBroken => Groups.Sum(g => g.Count);

    public void Load(IEnumerable<HealthEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Groups.Clear();
        foreach (var group in entries.GroupBy(e => e.Codepage).OrderByDescending(g => g.Count()))
            Groups.Add(new HealthGroupViewModel(group.Key, group.ToList()));
        OnPropertyChanged(nameof(TotalBroken));
    }

    [RelayCommand]
    public async Task FixAllAsync(HealthGroupViewModel? group)
    {
        if (group is null || fixBatch is null)
            return;
        await fixBatch(group.Entries).ConfigureAwait(true);
    }
}
