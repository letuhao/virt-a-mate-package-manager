using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Activation;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-12 · Activity screen lists audit records. (16-checklist SCR-12.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ActivityScreenTests
{
    private sealed class StubLog : IActivityLog
    {
        public Task RecordAsync(string k, string d, long? p = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ActivityRecord>> GetRecentAsync(int limit = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActivityRecord>>([new ActivityRecord("migrate", "T3 → T1 verified", DateTime.UtcNow)]);
    }

    [AvaloniaFact]
    public async Task Lists_audit_records()
    {
        var vm = new ActivityViewModel(new StubLog());
        await vm.RefreshAsync();
        var view = new ActivityView { DataContext = vm };
        var window = new Window { Width = 700, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("T3 → T1 verified", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
