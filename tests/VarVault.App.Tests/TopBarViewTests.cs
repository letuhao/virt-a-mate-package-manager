using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-3 · Top bar: search + rescue/jobs/add-repo/theme buttons; jobs dot reflects active jobs. (16-checklist SH-3.)</summary>
[Trait("Category", TestCategories.Unit)]
public class TopBarViewTests
{
    [AvaloniaFact]
    public void Renders_controls_and_binds_commands()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        var bar = new TopBarView { DataContext = shell };
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(bar.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()); // search
        var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.Command is not null).ToList();
        Assert.True(buttons.Count >= 4); // rescue, jobs, add-repo, theme
    }

    [AvaloniaFact]
    public void Jobs_dot_reflects_active_jobs()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>()) { HasActiveJobs = true };
        var bar = new TopBarView { DataContext = shell };
        var window = new Window { Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var dot = bar.GetVisualDescendants().OfType<Ellipse>().First();
        Assert.True(dot.IsVisible);

        shell.HasActiveJobs = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(dot.IsVisible);
    }

    [AvaloniaFact]
    public void Toggle_jobs_and_theme_commands_work()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        Assert.False(shell.JobsPanelOpen);
        shell.ToggleJobsCommand.Execute(null);
        Assert.True(shell.JobsPanelOpen);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        shell.ToggleThemeCommand.Execute(null);
        Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
    }
}
