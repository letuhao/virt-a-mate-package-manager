using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Threading;
using VarVault.App.Controls;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SH-4 · Jobs panel binds live jobs with progress; pause-all cancels. (16-checklist SH-4.)</summary>
[Trait("Category", TestCategories.Unit)]
public class JobsPanelViewTests
{
    [AvaloniaFact]
    public void Panel_renders_job_rows_with_progress()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>()) { JobsPanelOpen = true };
        var h1 = new JobHandle("Indexing — Seagate 8TB");
        h1.Report(new ProgressReport(18204, 26455, "stage 2"));
        var h2 = new JobHandle("Migrating T1 → T3");
        h2.Report(new ProgressReport(106, 312, "copy → verify"));
        shell.RefreshJobs([h1, h2]);

        var panel = new JobsPanelView { DataContext = shell };
        var window = new Window { Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rows = panel.GetVisualDescendants().OfType<JobRow>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Title == "Indexing — Seagate 8TB");
        Assert.Contains(rows, r => r.Fraction > 0);
    }

    [AvaloniaFact]
    public void Pause_all_cancels_every_job()
    {
        var shell = new ShellViewModel(new Dictionary<string, object>());
        var h = new JobHandle("Batch fix");
        shell.RefreshJobs([h]);

        shell.PauseAllCommand.Execute(null);
        Assert.True(h.Cancellation.IsCancellationRequested);
    }
}
