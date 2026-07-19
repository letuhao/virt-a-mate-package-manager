using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-8 · Proposals screen renders proposal cards with approve/reject. (16-checklist SCR-8.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ProposalsScreenTests
{
    private sealed class StubProposals : IProposalService
    {
        public Task<IReadOnlyList<Proposal>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Proposal>>([new("dedup:X", ProposalKind.Dedup, "Remove 4,120 duplicate copies", "312 GB · verified", 312, [1])]);
        public Task<ProposalActionResult> ApproveAsync(Proposal p, CancellationToken ct = default) => Task.FromResult(new ProposalActionResult(true, "done"));
        public Task<ProposalActionResult> RejectAsync(Proposal p, CancellationToken ct = default) => Task.FromResult(new ProposalActionResult(true, "rejected"));
    }

    [AvaloniaFact]
    public async Task Renders_proposal_cards()
    {
        var vm = new ProposalsViewModel(new StubProposals());
        await vm.LoadAsync();
        var view = new ProposalsView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Remove 4,120 duplicate copies", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
