using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-2 · Card renders its header (h3) and body content. (16-checklist SC-2.)</summary>
[Trait("Category", TestCategories.Unit)]
public class CardControlTests
{
    [AvaloniaFact]
    public void Card_renders_header_and_content()
    {
        var card = new Card { Header = "Storage by tier", Content = new TextBlock { Text = "body-text" } };
        var window = new Window { Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Storage by tier", texts); // header rendered
        Assert.Contains("body-text", texts);        // content rendered
        Assert.NotNull(card.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void Card_without_header_hides_the_title()
    {
        var card = new Card { Content = new TextBlock { Text = "only-body" } };
        var window = new Window { Content = card };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The header TextBlock is collapsed when Header is null/empty.
        var headerVisible = card.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsVisible && string.IsNullOrEmpty(t.Text));
        Assert.False(headerVisible);
    }
}
