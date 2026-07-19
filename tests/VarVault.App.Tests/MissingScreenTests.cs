using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-10 · Missing deps screen lists unresolved refs. (16-checklist SCR-10.)</summary>
[Trait("Category", TestCategories.Unit)]
public class MissingScreenTests
{
    private sealed class StubMissing : IMissingDepsQuery
    {
        public Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MissingDependency>>([new MissingDependency("Gone.Deleted.1", 83)]);
    }

    [AvaloniaFact]
    public async Task Lists_missing_refs()
    {
        var vm = new MissingDepsViewModel(new StubMissing());
        await vm.RefreshAsync();
        var view = new MissingView { DataContext = vm };
        var window = new Window { Width = 700, Height = 400, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Gone.Deleted.1", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
