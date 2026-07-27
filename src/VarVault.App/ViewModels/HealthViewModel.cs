using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.App.ViewModels;

/// <summary>SCR-9 · Health &amp; fix: encoding groups + integrity + live VaM-load scan. (16-checklist SCR-9.)</summary>
public sealed partial class HealthViewModel(
    IHealthService health,
    Services.IDialogLauncher? launcher = null,
    IJobQueue? jobs = null,
    Services.IFileReveal? reveal = null,
    IServiceScopeFactory? scopes = null) : ObservableObject, ILoadableScreen
{
    public PagedListState<IntegrityIssue> IntegrityPager { get; } =
        new((request, ct) => health.IntegrityPageAsync(request, ct));
    public PagedListState<IntegrityIssue> MissingMetaPager { get; } =
        new((request, ct) => health.MissingMetaPageAsync(request, ct));
    /// <summary>Sub-navigation tabs (GC-2).</summary>
    public IReadOnlyList<Controls.TabItemModel> Tabs { get; } =
        [new("Encoding"), new("Integrity / corrupt"), new("Missing meta"), new("VaM load")];
    [ObservableProperty] private int _selectedTabIndex;

    public bool IsEncodingTab => SelectedTabIndex == 0;
    public bool IsIntegrityTab => SelectedTabIndex == 1;
    public bool IsMissingMetaTab => SelectedTabIndex == 2;
    public bool IsVamLoadTab => SelectedTabIndex == 3;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsEncodingTab));
        OnPropertyChanged(nameof(IsIntegrityTab));
        OnPropertyChanged(nameof(IsMissingMetaTab));
        OnPropertyChanged(nameof(IsVamLoadTab));
    }

    [RelayCommand]
    private void FixGroup(EncodingGroup group)
    {
        if (group is not null)
            launcher?.OpenFix(0, group.Codepage);
    }

    [RelayCommand] private void FixAll() => launcher?.OpenFix(0, null);
    [RelayCommand] private void FixGbk() => launcher?.OpenFix(0, "GB");
    [RelayCommand] private void FixShiftJis() => launcher?.OpenFix(0, "Shift");

    public ObservableCollection<EncodingGroup> EncodingGroups { get; } = [];
    public ObservableCollection<VamLoadIssue> VamLoadIssues { get; } = [];

    public int GbkCount => EncodingGroups.Where(g => g.Codepage.Contains("GB", StringComparison.OrdinalIgnoreCase)).Sum(g => g.Count);
    public int ShiftJisCount => EncodingGroups.Where(g => g.Codepage.Contains("Shift", StringComparison.OrdinalIgnoreCase) || g.Codepage.Contains("932", StringComparison.Ordinal)).Sum(g => g.Count);
    public int OtherEncodingCount => EncodingGroups.Sum(g => g.Count) - GbkCount - ShiftJisCount;

    public bool IsEmpty => EncodingGroups.Count == 0;
    public bool IsVamLoadEmpty => VamLoadIssues.Count == 0 && !IsScanning;
    public ObservableCollection<IntegrityIssue> Integrity => IntegrityPager.Items;
    public ObservableCollection<IntegrityIssue> MissingMeta => MissingMetaPager.Items;

    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _vamLoadStatus = "Run Scan installed to check active-profile packages (newest InstalledAt first).";

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsVamLoadEmpty));

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

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void ScanInstalled() => EnqueueScan(VamLoadScanScope.InstalledOnly, "VaM-load: installed");

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void ScanAll() => EnqueueScan(VamLoadScanScope.AllCatalog, "VaM-load: all catalog");

    private bool CanScan() => !IsScanning;

    private void EnqueueScan(VamLoadScanScope scope, string jobName)
    {
        if (IsScanning)
            return;

        IsScanning = true;
        ScanInstalledCommand.NotifyCanExecuteChanged();
        ScanAllCommand.NotifyCanExecuteChanged();
        FixAllDuplicatesCommand.NotifyCanExecuteChanged();
        VamLoadStatus = "Scanning…";
        OnPropertyChanged(nameof(IsVamLoadEmpty));

        if (jobs is null)
        {
            _ = RunScanUiAsync(scope);
            return;
        }

        jobs.Enqueue(jobName, async ctx =>
        {
            try
            {
                var progress = new UiProgressSink(msg =>
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => VamLoadStatus = msg));
                IReadOnlyList<VamLoadIssue> found;
                if (scopes is not null)
                {
                    using var scopeSvc = scopes.CreateScope();
                    var scopedHealth = scopeSvc.ServiceProvider.GetRequiredService<IHealthService>();
                    found = await scopedHealth.ScanVamLoadAsync(scope, progress, ctx.Cancellation).ConfigureAwait(false);
                }
                else
                {
                    found = await health.ScanVamLoadAsync(scope, progress, ctx.Cancellation).ConfigureAwait(false);
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyScanResults(found));
            }
            catch (OperationCanceledException)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScanning = false;
                    VamLoadStatus = "Scan cancelled.";
                    ScanInstalledCommand.NotifyCanExecuteChanged();
                    ScanAllCommand.NotifyCanExecuteChanged();
                    FixAllDuplicatesCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(IsVamLoadEmpty));
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScanning = false;
                    VamLoadStatus = $"Scan failed: {ex.Message}";
                    ScanInstalledCommand.NotifyCanExecuteChanged();
                    ScanAllCommand.NotifyCanExecuteChanged();
                    FixAllDuplicatesCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(IsVamLoadEmpty));
                });
            }
        });
    }

    private async Task RunScanUiAsync(VamLoadScanScope scope)
    {
        try
        {
            var found = await health.ScanVamLoadAsync(scope, cancellationToken: default).ConfigureAwait(true);
            ApplyScanResults(found);
        }
        catch (OperationCanceledException)
        {
            IsScanning = false;
            VamLoadStatus = "Scan cancelled.";
            ScanInstalledCommand.NotifyCanExecuteChanged();
            ScanAllCommand.NotifyCanExecuteChanged();
            FixAllDuplicatesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsVamLoadEmpty));
        }
        catch (Exception ex)
        {
            IsScanning = false;
            VamLoadStatus = $"Scan failed: {ex.Message}";
            ScanInstalledCommand.NotifyCanExecuteChanged();
            ScanAllCommand.NotifyCanExecuteChanged();
            FixAllDuplicatesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(IsVamLoadEmpty));
        }
    }

    private void ApplyScanResults(IReadOnlyList<VamLoadIssue> found)
    {
        VamLoadIssues.Clear();
        foreach (var issue in found)
            VamLoadIssues.Add(issue);
        IsScanning = false;
        VamLoadStatus = found.Count == 0
            ? "No VaM-load defects found."
            : $"{found.Count} finding(s). Use Fix on DuplicateEntries rows.";
        ScanInstalledCommand.NotifyCanExecuteChanged();
        ScanAllCommand.NotifyCanExecuteChanged();
        FixAllDuplicatesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsVamLoadEmpty));
        _ = LoadAsync();
    }

    [RelayCommand]
    private void RevealIssue(VamLoadIssue? issue)
    {
        if (issue is null || string.IsNullOrWhiteSpace(issue.FullPath))
            return;
        reveal?.Reveal(issue.FullPath);
    }

    [RelayCommand]
    private void OpenIssue(VamLoadIssue? issue)
    {
        if (issue?.PackageId is long pkgId)
            launcher?.OpenVarDetail(pkgId);
    }

    [RelayCommand]
    private void FixDuplicateIssue(VamLoadIssue? issue)
    {
        if (issue is null || !string.Equals(issue.Kind, "DuplicateEntries", StringComparison.Ordinal))
            return;
        launcher?.OpenDedupFix(issue.VarFileId, issue.VarName, onFixed: () =>
        {
            VamLoadStatus = "Dedup-fix applied — original retained as .dedup.var lineage.";
            _ = LoadAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void FixAllDuplicates()
    {
        var ids = VamLoadIssues
            .Where(i => string.Equals(i.Kind, "DuplicateEntries", StringComparison.Ordinal))
            .Select(i => i.VarFileId)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            VamLoadStatus = "No DuplicateEntries findings to fix — run a scan first.";
            return;
        }
        EnqueueDedupFix(ids, $"Dedup-fix ({ids.Count})");
    }

    private void EnqueueDedupFix(IReadOnlyList<long> ids, string jobName)
    {
        if (IsScanning)
            return;
        IsScanning = true;
        ScanInstalledCommand.NotifyCanExecuteChanged();
        ScanAllCommand.NotifyCanExecuteChanged();
        FixAllDuplicatesCommand.NotifyCanExecuteChanged();
        VamLoadStatus = "Fixing duplicates…";

        async Task RunAsync(IHealthService svc, CancellationToken ct)
        {
            var result = await svc.FixDuplicateEntriesManyAsync(ids, cancellationToken: ct).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsScanning = false;
                VamLoadStatus = $"Dedup-fix done · {result.Succeeded} fixed · {result.Failed} skipped · originals retained as .dedup.var lineage";
                ScanInstalledCommand.NotifyCanExecuteChanged();
                ScanAllCommand.NotifyCanExecuteChanged();
                FixAllDuplicatesCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsVamLoadEmpty));
                _ = LoadAsync();
            });
        }

        if (jobs is null)
        {
            _ = RunAsync(health, default);
            return;
        }

        jobs.Enqueue(jobName, async ctx =>
        {
            try
            {
                if (scopes is not null)
                {
                    using var scopeSvc = scopes.CreateScope();
                    await RunAsync(scopeSvc.ServiceProvider.GetRequiredService<IHealthService>(), ctx.Cancellation)
                        .ConfigureAwait(false);
                }
                else
                {
                    await RunAsync(health, ctx.Cancellation).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScanning = false;
                    VamLoadStatus = "Dedup-fix cancelled.";
                    ScanInstalledCommand.NotifyCanExecuteChanged();
                    ScanAllCommand.NotifyCanExecuteChanged();
                    FixAllDuplicatesCommand.NotifyCanExecuteChanged();
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsScanning = false;
                    VamLoadStatus = $"Dedup-fix failed: {ex.Message}";
                    ScanInstalledCommand.NotifyCanExecuteChanged();
                    ScanAllCommand.NotifyCanExecuteChanged();
                    FixAllDuplicatesCommand.NotifyCanExecuteChanged();
                });
            }
        });
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

    private sealed class UiProgressSink(Action<string> onMessage) : IProgressSink
    {
        public void Report(ProgressReport value)
        {
            if (!string.IsNullOrWhiteSpace(value.Message))
                onMessage(value.Message!);
        }
    }
}
