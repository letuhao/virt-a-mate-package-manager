using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>SCR-9 · Health &amp; fix: encoding groups + integrity issues; fix into UTF-8. (16-checklist SCR-9.)</summary>
public sealed partial class HealthViewModel(
    IHealthService health, Services.IDialogLauncher? launcher = null) : ObservableObject, ILoadableScreen
{
    public PagedListState<IntegrityIssue> IntegrityPager { get; } =
        new((request, ct) => health.IntegrityPageAsync(request, ct));
    public PagedListState<IntegrityIssue> MissingMetaPager { get; } =
        new((request, ct) => health.MissingMetaPageAsync(request, ct));
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
    public ObservableCollection<IntegrityIssue> Integrity => IntegrityPager.Items;

    /// <summary>Vars missing a parseable meta.json, for the Missing-meta tab. (24-checklist A4)</summary>
    public ObservableCollection<IntegrityIssue> MissingMeta => MissingMetaPager.Items;

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
        await IntegrityPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        await MissingMetaPager.ResetAndReloadAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    public async Task FixAsync(long varFileId, CancellationToken cancellationToken = default)
    {
        var result = await health.FixAsync(varFileId, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Fixed" : result.Error.Message;
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanIntegrityPreviousPage))]
    private async Task IntegrityPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await IntegrityPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanIntegrityNextPage))]
    private async Task IntegrityNextPageAsync(CancellationToken cancellationToken = default)
    {
        await IntegrityPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task IntegrityGoToPageAsync(int pageNumber)
    {
        await IntegrityPager.LoadPageAsync(pageNumber, IntegrityPager.PageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task IntegrityChangePageSizeAsync(int pageSize)
    {
        await IntegrityPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanMissingMetaPreviousPage))]
    private async Task MissingMetaPreviousPageAsync(CancellationToken cancellationToken = default)
    {
        await MissingMetaPager.PreviousPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand(CanExecute = nameof(CanMissingMetaNextPage))]
    private async Task MissingMetaNextPageAsync(CancellationToken cancellationToken = default)
    {
        await MissingMetaPager.NextPageAsync(cancellationToken).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task MissingMetaGoToPageAsync(int pageNumber)
    {
        await MissingMetaPager.LoadPageAsync(pageNumber, MissingMetaPager.PageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    [RelayCommand]
    private async Task MissingMetaChangePageSizeAsync(int pageSize)
    {
        await MissingMetaPager.LoadPageAsync(1, pageSize).ConfigureAwait(true);
        NotifyPaged();
    }

    private bool CanIntegrityPreviousPage() => IntegrityPager.HasPreviousPage && !IntegrityPager.IsLoading;
    private bool CanIntegrityNextPage() => IntegrityPager.HasNextPage && !IntegrityPager.IsLoading;
    private bool CanMissingMetaPreviousPage() => MissingMetaPager.HasPreviousPage && !MissingMetaPager.IsLoading;
    private bool CanMissingMetaNextPage() => MissingMetaPager.HasNextPage && !MissingMetaPager.IsLoading;

    private void NotifyPaged()
    {
        IntegrityPreviousPageCommand.NotifyCanExecuteChanged();
        IntegrityNextPageCommand.NotifyCanExecuteChanged();
        MissingMetaPreviousPageCommand.NotifyCanExecuteChanged();
        MissingMetaNextPageCommand.NotifyCanExecuteChanged();
    }
}
