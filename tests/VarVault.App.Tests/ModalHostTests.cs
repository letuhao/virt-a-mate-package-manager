using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-9 · ModalHost: open shows; Esc, ×, and backdrop-click close; content/footer render. (16-checklist SC-9.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ModalHostTests
{
    private static (Window window, ModalHost host) Show(bool open)
    {
        var host = new ModalHost
        {
            Title = "Delete 3 packages?",
            Content = new TextBlock { Text = "body" },
            Footer = new Button { Content = "Cancel" },
            IsOpen = open,
        };
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, host);
    }

    [AvaloniaFact]
    public void Open_shows_backdrop_title_and_content()
    {
        var (_, host) = Show(open: true);
        var backdrop = host.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Backdrop");
        Assert.True(backdrop.IsVisible);
        var texts = host.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Delete 3 packages?", texts);
        Assert.Contains("body", texts);
    }

    [AvaloniaFact]
    public void Closed_hides_the_backdrop()
    {
        var (_, host) = Show(open: false);
        var backdrop = host.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Backdrop");
        Assert.False(backdrop.IsVisible);
    }

    [AvaloniaFact]
    public void Escape_closes()
    {
        var (window, host) = Show(open: true);
        host.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsOpen);
    }

    [AvaloniaFact]
    public void Close_button_closes()
    {
        var (_, host) = Show(open: true);
        var close = host.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Close");
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.False(host.IsOpen);
    }

    [AvaloniaFact]
    public void Backdrop_click_closes()
    {
        var (window, host) = Show(open: true);
        // Click the top-left corner — the backdrop, not the centered dialog.
        window.MouseDown(new Point(5, 5), MouseButton.Left);
        window.MouseUp(new Point(5, 5), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(host.IsOpen);
    }
}
