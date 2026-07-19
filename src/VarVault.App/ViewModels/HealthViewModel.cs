using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-9 · Health &amp; fix: encoding groups + integrity issues; fix into UTF-8. (16-checklist SCR-9.)</summary>
public sealed partial class HealthViewModel(IHealthService health) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Encoding"), new("Integrity / corrupt"), new("Missing meta")];
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<EncodingGroup> EncodingGroups { get; } = [];
    public ObservableCollection<IntegrityIssue> Integrity { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        EncodingGroups.Clear();
        foreach (var g in await health.EncodingGroupsAsync(cancellationToken).ConfigureAwait(true))
            EncodingGroups.Add(g);
        Integrity.Clear();
        foreach (var i in await health.IntegrityAsync(cancellationToken).ConfigureAwait(true))
            Integrity.Add(i);
    }

    [RelayCommand]
    public async Task FixAsync(long varFileId, CancellationToken cancellationToken = default)
    {
        var result = await health.FixAsync(varFileId, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Fixed" : result.Error.Message;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
