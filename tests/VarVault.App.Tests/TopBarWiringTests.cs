using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GA-6 · The top-bar handlers are wired: Rescue and Add-repository open their dialogs through the dialog
/// service (they used to invoke null), and search-Enter opens the palette. (18-gap GA-6.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public class TopBarWiringTests
{
    private static ShellViewModel Composed(out IServiceScope scope, TestHost host)
    {
        scope = host.Host.Services.CreateScope();
        return AppHost.CreateShell(scope.ServiceProvider);
    }

    [Fact]
    public async Task Add_repo_opens_the_add_repo_dialog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var shell = Composed(out var scope, host);
        using (scope)
        {
            Assert.False(shell.Dialogs.IsOpen);
            shell.AddRepoCommand.Execute(null);
            Assert.True(shell.Dialogs.IsOpen);
            Assert.IsType<AddRepoViewModel>(shell.Dialogs.Current);
        }
    }

    [Fact]
    public async Task Rescue_opens_the_rescue_dialog()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var shell = Composed(out var scope, host);
        using (scope)
        {
            await shell.RescueCommand.ExecuteAsync(null);
            Assert.True(shell.Dialogs.IsOpen);
            Assert.IsType<RescueViewModel>(shell.Dialogs.Current);
        }
    }

    [Fact]
    public void Search_enter_opens_the_palette()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        Assert.False(shell.PaletteOpen);
        shell.OpenPaletteCommand.Execute(null);
        Assert.True(shell.PaletteOpen);
    }
}
