using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Threading;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SC-12 · JobRow binds a JobHandle (title/progress/sub-line); cancel cancels the job. (16-checklist SC-12.)</summary>
[Trait("Category", TestCategories.Unit)]
public class JobRowTests
{
    [AvaloniaFact]
    public void Binds_handle_title_progress_and_subline()
    {
        var handle = new JobHandle("Indexing — Seagate 8TB");
        handle.Report(new ProgressReport(18204, 26455, "stage 2 previews"));

        var row = new JobRow { Handle = handle };
        var window = new Window { Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Indexing — Seagate 8TB", row.Title);
        Assert.Equal(18204d / 26455d, row.Fraction, 4);
        Assert.Contains("stage 2 previews", row.SubLine);
        Assert.Contains("18,204", row.SubLine);
    }

    [AvaloniaFact]
    public void Cancel_button_cancels_the_job_token()
    {
        var handle = new JobHandle("Migrating");
        var context = new JobContext(handle.Cancellation, handle); // as the queue would build it
        var row = new JobRow { Handle = handle };
        var window = new Window { Content = row };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var cancel = row.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Cancel");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(context.Cancellation.IsCancellationRequested); // JobContext token cancelled
    }

    [AvaloniaFact]
    public void Refresh_repicks_updated_progress()
    {
        var handle = new JobHandle("Batch fix");
        handle.Report(new ProgressReport(52, 431, "GBK → UTF-8"));
        var row = new JobRow { Handle = handle };

        handle.Report(new ProgressReport(200, 431, "GBK → UTF-8"));
        row.Refresh();

        Assert.Equal(200d / 431d, row.Fraction, 4);
    }
}
