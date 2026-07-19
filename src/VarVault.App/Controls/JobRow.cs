using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using VarVault.Sdk.Threading;

namespace VarVault.App.Controls;

/// <summary>
/// SC-12 · Job row (prototype jobs-panel job): title + cancel × + progress bar + sub-line, bound to a
/// <see cref="JobHandle"/>. <see cref="JobHandle"/> isn't observable, so <see cref="Refresh"/> re-reads it
/// (the jobs panel polls). Cancel calls <see cref="JobHandle.Cancel"/>, which cancels the job's
/// <see cref="JobContext.Cancellation"/>. (16-checklist SC-12.)
/// </summary>
public class JobRow : TemplatedControl
{
    public static readonly StyledProperty<JobHandle?> HandleProperty =
        AvaloniaProperty.Register<JobRow, JobHandle?>(nameof(Handle));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<JobRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubLineProperty =
        AvaloniaProperty.Register<JobRow, string?>(nameof(SubLine));

    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<JobRow, double>(nameof(Fraction));

    public JobHandle? Handle { get => GetValue(HandleProperty); set => SetValue(HandleProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? SubLine { get => GetValue(SubLineProperty); set => SetValue(SubLineProperty, value); }
    public double Fraction { get => GetValue(FractionProperty); set => SetValue(FractionProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>("PART_Cancel") is { } cancel)
            cancel.Click += (_, _) => Handle?.Cancel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HandleProperty)
            Refresh();
    }

    /// <summary>Re-read the (non-observable) handle into the display properties. Called by the polling panel.</summary>
    public void Refresh()
    {
        if (Handle is not { } h)
        {
            Title = null; SubLine = null; Fraction = 0;
            return;
        }
        Title = h.Name;
        var p = h.Progress;
        Fraction = p.Fraction;
        SubLine = p.Message is { Length: > 0 }
            ? (p.Total > 0 ? $"{p.Message} · {p.Done:N0}/{p.Total:N0}" : p.Message)
            : (p.Total > 0 ? $"{p.Done:N0}/{p.Total:N0}" : null);
    }
}
