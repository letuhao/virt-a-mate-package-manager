using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-9 · Health &amp; fix: encoding groups + integrity issues; fix into UTF-8. (16-checklist SCR-9.)</summary>
public sealed partial class HealthViewModel(
    IHealthService health, Services.IDialogLauncher? launcher = null) : ObservableObject
{
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Encoding"), new("Integrity / corrupt"), new("Missing meta")];
    [ObservableProperty] private int _selectedTabIndex;

    // Per-tab visibility so switching a tab actually swaps content. (24-checklist A1/A3)
    public bool IsEncodingTab => SelectedTabIndex == 0;
    public bool IsIntegrityTab => SelectedTabIndex == 1;
    public bool IsMissingMetaTab => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEncodingTab));
        OnPropertyChanged(nameof(IsIntegrityTab));
        OnPropertyChanged(nameof(IsMissingMetaTab));
    }

    /// <summary>Per-group "Fix group…" → fix-encoding dialog for the codepage. (GD-11)</summary>
    [RelayCommand]
    private void FixGroup(EncodingGroup group)
    {
        if (group is not null)
            launcher?.OpenFix(0, group.Codepage);
    }

    /// <summary>Screen-head "Fix all detected…" → fix-encoding dialog. (GD-11)</summary>
    [RelayCommand] private void FixAll() => launcher?.OpenFix(0, null);

    public ObservableCollection<EncodingGroup> EncodingGroups { get; } = [];

    // Summary-card counts by codepage family (AC-16).
    public int GbkCount => EncodingGroups.Where(g => g.Codepage.Contains("GB", StringComparison.OrdinalIgnoreCase)).Sum(g => g.Count);
    public int ShiftJisCount => EncodingGroups.Where(g => g.Codepage.Contains("Shift", StringComparison.OrdinalIgnoreCase) || g.Codepage.Contains("932", StringComparison.Ordinal)).Sum(g => g.Count);
    public int OtherEncodingCount => EncodingGroups.Sum(g => g.Count) - GbkCount - ShiftJisCount;

    /// <summary>Explicit empty-state flag for the encoding groups. (GF-2)</summary>
    public bool IsEmpty => EncodingGroups.Count == 0;
    public ObservableCollection<IntegrityIssue> Integrity { get; } = [];

    /// <summary>Vars missing a parseable meta.json, for the Missing-meta tab. (24-checklist A4)</summary>
    public ObservableCollection<IntegrityIssue> MissingMeta { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        EncodingGroups.Clear();
        foreach (var g in await health.EncodingGroupsAsync(cancellationToken).ConfigureAwait(true))
            EncodingGroups.Add(g);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(GbkCount));
        OnPropertyChanged(nameof(ShiftJisCount));
        OnPropertyChanged(nameof(OtherEncodingCount));
        Integrity.Clear();
        foreach (var i in await health.IntegrityAsync(cancellationToken).ConfigureAwait(true))
            Integrity.Add(i);
        MissingMeta.Clear();
        foreach (var i in await health.MissingMetaAsync(cancellationToken).ConfigureAwait(true))
            MissingMeta.Add(i);
    }

    [RelayCommand]
    public async Task FixAsync(long varFileId, CancellationToken cancellationToken = default)
    {
        var result = await health.FixAsync(varFileId, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Fixed" : result.Error.Message;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
