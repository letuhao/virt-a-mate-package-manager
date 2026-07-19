using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-10 · Toast: shows message, Undo fires callback, auto-dismiss hides. (16-checklist SC-10.)</summary>
[Trait("Category", TestCategories.Unit)]
public class ToastControlTests
{
    [AvaloniaFact]
    public void Show_displays_message_and_undo()
    {
        var toast = new Toast((_, _) => Task.Delay(-1, System.Threading.CancellationToken.None)); // never auto-dismiss
        var window = new Window { Content = toast };
        window.Show();

        toast.Show("Moved 2 items to trash", undo: () => { });
        Dispatcher.UIThread.RunJobs();

        Assert.True(toast.IsOpen);
        Assert.True(toast.HasUndo);
        var texts = toast.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Moved 2 items to trash", texts);
    }

    [AvaloniaFact]
    public void Show_without_undo_hides_undo_button()
    {
        var toast = new Toast((_, ct) => Task.Delay(-1, ct));
        toast.Show("Indexed 277 vars");
        Assert.False(toast.HasUndo);
    }

    [AvaloniaFact]
    public void Undo_invokes_callback_and_hides()
    {
        var undone = false;
        var toast = new Toast((_, ct) => Task.Delay(-1, ct));
        toast.Show("Deleted", undo: () => undone = true);

        toast.Undo();

        Assert.True(undone);
        Assert.False(toast.IsOpen);
    }

    [AvaloniaFact]
    public async Task Auto_dismiss_hides_after_the_delay()
    {
        // Gated delay: dismiss cannot happen until we release, so the interim open state is deterministic.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var toast = new Toast((_, ct) => { ct.Register(() => gate.TrySetCanceled()); return gate.Task; });

        toast.Show("Fixed encoding");
        Assert.True(toast.IsOpen);       // still open before the delay settles

        gate.SetResult();
        await toast.PendingDismiss!;
        Assert.False(toast.IsOpen);      // auto-dismissed
    }
}
