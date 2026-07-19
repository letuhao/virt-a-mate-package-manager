using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Composition;
using VarVault.App.Controls;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Activation;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GA-1 · The dialog service opens/closes modals through the single ModalHost, and every one of the 10
/// dialog view-models maps to a concrete view. (18-gap GA-1.)
/// </summary>
public class DialogServiceTests
{
    private sealed class StubActivation : IActivationService
    {
        public Task<int> RescueAsync(long profileId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<ActivationBuildResult> BuildProfileLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ActivationBuildResult> DeactivateAsync(long p, long pkg, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CleanTempLinksAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public async Task Show_makes_modal_visible_and_hosts_the_vm()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var modal = window.GetVisualDescendants().OfType<ModalHost>().Single();
        Assert.False(modal.IsOpen); // closed initially

        var dialogVm = new RescueViewModel(new StubActivation());
        shell.Dialogs.Show(dialogVm);
        Dispatcher.UIThread.RunJobs();

        Assert.True(modal.IsOpen);
        Assert.Same(dialogVm, shell.Dialogs.Current);
        // The backdrop is visible and the dialog view resolved from the VM.
        var backdrop = modal.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Backdrop");
        Assert.True(backdrop.IsVisible);
        Assert.NotEmpty(modal.GetVisualDescendants().OfType<RescueDialog>());
    }

    [AvaloniaFact]
    public async Task Close_and_Esc_hide_it()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var modal = window.GetVisualDescendants().OfType<ModalHost>().Single();

        shell.Dialogs.Show(new RescueViewModel(new StubActivation()));
        Dispatcher.UIThread.RunJobs();
        Assert.True(modal.IsOpen);

        // Service close hides it.
        shell.Dialogs.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(modal.IsOpen);
        Assert.Null(shell.Dialogs.Current);

        // Esc on the host also closes.
        shell.Dialogs.Show(new RescueViewModel(new StubActivation()));
        Dispatcher.UIThread.RunJobs();
        modal.Focus();
        modal.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(modal.IsOpen);
    }

    [AvaloniaFact]
    public void Show_stores_and_close_clears_the_current_vm()
    {
        var svc = new DialogService();
        Assert.False(svc.IsOpen);
        var vm = new object();
        svc.Show(vm);
        Assert.True(svc.IsOpen);
        Assert.Same(vm, svc.Current);
        svc.Close();
        Assert.False(svc.IsOpen);
        Assert.Null(svc.Current);
    }
}
