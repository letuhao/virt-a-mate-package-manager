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
        public Task<int> ReconcileProfilesAsync(CancellationToken ct = default) => Task.FromResult(0);
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

        // Esc on the host also closes through the service (not just the visual host).
        shell.Dialogs.Show(new RescueViewModel(new StubActivation()));
        Dispatcher.UIThread.RunJobs();
        modal.Focus();
        modal.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();
        Assert.False(modal.IsOpen);
        Assert.False(shell.Dialogs.IsOpen);
        Assert.Null(shell.Dialogs.Current);

        // Reopen after Esc — must show again (regression for modal lifecycle divergence).
        shell.Dialogs.Show(new RescueViewModel(new StubActivation()));
        Dispatcher.UIThread.RunJobs();
        Assert.True(modal.IsOpen);
        Assert.True(shell.Dialogs.IsOpen);
        Assert.NotNull(shell.Dialogs.Current);
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

    [Fact]
    public void Push_and_back_restore_previous_dialog()
    {
        var svc = new DialogService();
        var first = new object();
        var second = new object();
        svc.Show(first);
        svc.Push(second);
        Assert.Same(second, svc.Current);
        Assert.True(svc.CanGoBack);
        svc.Back();
        Assert.Same(first, svc.Current);
        Assert.False(svc.CanGoBack);
    }

    [AvaloniaFact]
    public async Task Nested_Esc_backs_then_closes()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var shell = AppHost.CreateShell(scope.ServiceProvider);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var modal = window.GetVisualDescendants().OfType<ModalHost>().Single();

        var parent = new RescueViewModel(new StubActivation());
        var child = new object();
        shell.Dialogs.Show(parent);
        shell.Dialogs.Push(child);
        Dispatcher.UIThread.RunJobs();
        Assert.True(modal.IsOpen);
        Assert.Same(child, shell.Dialogs.Current);
        Assert.True(shell.Dialogs.CanGoBack);

        // First Esc/close-request pops the nested dialog.
        modal.RequestClose();
        Dispatcher.UIThread.RunJobs();
        Assert.True(modal.IsOpen);
        Assert.Same(parent, shell.Dialogs.Current);
        Assert.False(shell.Dialogs.CanGoBack);

        // Second Esc closes the stack.
        modal.RequestClose();
        Dispatcher.UIThread.RunJobs();
        Assert.False(modal.IsOpen);
        Assert.Null(shell.Dialogs.Current);
    }
}
