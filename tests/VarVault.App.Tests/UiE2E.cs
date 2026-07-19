using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VarVault.App.Tests;

/// <summary>
/// Helpers for real UI-E2E: these tests <b>run the actual application window</b> (real <see cref="App"/>,
/// real <c>MainWindow</c>, real SQLite-backed services) and drive real controls, then capture the rendered
/// frame as PNG evidence. This is the "run and use the app" proof the audit-correction slices require — not
/// view-model unit assertions. (19-Audit-Correction.)
/// </summary>
public static class UiE2E
{
    /// <summary>Repo-relative folder where per-slice screenshots land, so they are reviewable in the IDE.</summary>
    public static string EvidenceDir
    {
        get
        {
            // tests/VarVault.App.Tests/bin/Debug/net10.0 → repo root is five levels up.
            var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "docs", "new-app", "ui-evidence"));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Pump the UI thread until queued work drains (layout, bindings, async continuations).</summary>
    public static void Pump()
    {
        for (var i = 0; i < 8; i++)
            Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Render the live window and save it as <c>{name}.png</c> under <see cref="EvidenceDir"/>.</summary>
    public static string Screenshot(Window window, string name)
    {
        Pump();
        var frame = window.CaptureRenderedFrame();
        var path = Path.Combine(EvidenceDir, name + ".png");
        frame?.Save(path);
        return path;
    }

    /// <summary>First visible control of type <typeparamref name="T"/> whose text/content matches.</summary>
    public static T? FindByContent<T>(Visual root, string content) where T : ContentControl =>
        root.GetVisualDescendants().OfType<T>()
            .FirstOrDefault(c => string.Equals(c.Content?.ToString(), content, StringComparison.Ordinal));

    /// <summary>First button whose Content string contains <paramref name="text"/> (loose match for glyphs).</summary>
    public static Button? Button(Visual root, string text) =>
        root.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Content?.ToString()?.Contains(text, StringComparison.Ordinal) == true);

    /// <summary>Click a button through the real control (raises its Command), then pump.</summary>
    public static void Click(Button button)
    {
        button.Command?.Execute(button.CommandParameter);
        Pump();
    }

    /// <summary>
    /// Drive the real button's bound command to completion. Awaits async commands deterministically (no
    /// sleep-then-assert) while still exercising exactly the command the view wired to the control.
    /// </summary>
    public static async Task ClickAsync(Button button)
    {
        if (button.Command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCmd)
            await asyncCmd.ExecuteAsync(button.CommandParameter);
        else
            button.Command?.Execute(button.CommandParameter);
        Pump();
    }
}
