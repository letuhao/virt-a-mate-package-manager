using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-9 · Health screen lists encoding groups. (16-checklist SCR-9.)</summary>
[Trait("Category", TestCategories.Unit)]
public class HealthScreenTests
{
    private sealed class StubHealth : IHealthService
    {
        public Task<IReadOnlyList<EncodingGroup>> EncodingGroupsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EncodingGroup>>([new EncodingGroup("GBK", 431), new EncodingGroup("Shift-JIS", 188)]);
        public Task<IReadOnlyList<IntegrityIssue>> IntegrityAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
        public Task<IReadOnlyList<IntegrityIssue>> MissingMetaAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<IntegrityIssue>>([]);
        public Task<IReadOnlyList<VamLoadIssue>> ScanVamLoadAsync(
            VamLoadScanScope scope, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VamLoadIssue>>([]);
        public Task<Result<long>> FixAsync(long id, CancellationToken ct = default) => Task.FromResult(Result.Success(1L));
        public Task<Result<long>> FixDuplicateEntriesAsync(
            long id,
            IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
            CancellationToken ct = default) => Task.FromResult(Result.Success(1L));
        public Task<Result<IReadOnlyList<DuplicateEntryCollision>>> GetDuplicateEntryCollisionsAsync(
            long varFileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success<IReadOnlyList<DuplicateEntryCollision>>([]));
        public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(0, 0));
        public Task<BulkActionResult> FixGroupAsync(string? codepageFilter, IProgressSink progress, CancellationToken ct = default) =>
            FixGroupAsync(codepageFilter, ct);
        public Task<BulkActionResult> FixManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(0, 0));
        public Task<BulkActionResult> FixDuplicateEntriesManyAsync(IReadOnlyList<long> varFileIds, IProgressSink? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new BulkActionResult(0, 0));
    }

    [AvaloniaFact]
    public async Task Lists_encoding_groups()
    {
        var vm = new HealthViewModel(new StubHealth());
        await vm.LoadAsync();
        Assert.Equal(2, vm.EncodingGroups.Count);

        var view = new HealthView { DataContext = vm };
        var window = new Window { Width = 700, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("GBK", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
