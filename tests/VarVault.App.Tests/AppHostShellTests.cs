using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-6 · AppHost composes the shell with all 13 screens non-null. (16-checklist SH-6.)</summary>
[Trait("Category", TestCategories.Integration)]
public class AppHostShellTests
{
    [Fact]
    public async System.Threading.Tasks.Task Shell_resolves_with_all_screens_non_null()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();

        var shell = AppHost.CreateShell(scope.ServiceProvider);

        Assert.NotNull(shell);
        Assert.Equal(13, shell.Screens.Count);

        // Every screen id resolves to a non-null view-model (real VM or placeholder).
        foreach (var screen in ShellViewModel.AllScreens)
        {
            shell.NavigateCommand.Execute(screen.Id);
            Assert.NotNull(shell.ActiveScreen);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Backend_ready_screens_use_real_view_models()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);

        shell.NavigateCommand.Execute("library");
        Assert.IsType<LibraryViewModel>(shell.ActiveScreen);
        shell.NavigateCommand.Execute("analytics");
        Assert.IsType<AnalyticsViewModel>(shell.ActiveScreen);
        shell.NavigateCommand.Execute("settings"); // no view yet → placeholder
        Assert.IsType<PlaceholderScreenViewModel>(shell.ActiveScreen);
    }
}
