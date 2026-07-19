using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.Controls;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// POL-4 · Shell smoke: the composed shell window launches, the rail renders, and every one of the 13
/// screens is navigable and shows a real view. (16-checklist POL-4.)
/// </summary>
public class MainWindowUiTests
{
    [AvaloniaFact]
    public async Task Shell_launches_and_every_screen_is_navigable()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Rail present with all 13 items.
        Assert.Equal(13, window.GetVisualDescendants().OfType<RailNavItem>().Count());

        // Visit every screen — the content host swaps to a non-null view each time.
        foreach (var screen in ShellViewModel.AllScreens)
        {
            shell.NavigateCommand.Execute(screen.Id);
            Dispatcher.UIThread.RunJobs();
            var content = window.GetVisualDescendants().OfType<ContentControl>()
                .FirstOrDefault(cc => ReferenceEquals(cc.Content, shell.ActiveScreen));
            Assert.NotNull(shell.ActiveScreen);
            Assert.NotNull(content);
        }
    }

    [AvaloniaFact]
    public async Task Shell_renders_in_both_theme_variants()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        var light = new MainWindow { DataContext = shell };
        light.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Light, light.ActualThemeVariant);

        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        var dark = new MainWindow { DataContext = shell };
        dark.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ThemeVariant.Dark, dark.ActualThemeVariant);
    }
}
