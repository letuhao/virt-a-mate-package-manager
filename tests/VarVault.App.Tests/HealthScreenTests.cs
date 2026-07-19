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
        public Task<Result<long>> FixAsync(long id, CancellationToken ct = default) => Task.FromResult(Result.Success(1L));
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
