using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace VarVault.App.Controls;

/// <summary>
/// SC-6 · Meter bar (prototype <c>.bar</c> / capacity / job bar): a track with a proportional fill.
/// <see cref="Value"/> is 0..1; width tracks value via star columns (no animation → reduced-motion safe).
/// (16-checklist SC-6.)
/// </summary>
public class MeterBar : TemplatedControl
{
    private Grid? _track;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<MeterBar, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<MeterBar, IBrush?>(nameof(Fill));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Grid>("PART_Track");
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == FillProperty)
            Apply();
    }

    private void Apply()
    {
        if (_track is null)
            return;
        var v = Math.Clamp(Value, 0, 1);
        _track.ColumnDefinitions = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(v, GridUnitType.Star)),
            new ColumnDefinition(new GridLength(1 - v, GridUnitType.Star)),
        };
        _track.Children.Clear();
        var fill = new Border { Background = Fill ?? Brushes.Gray, CornerRadius = new CornerRadius(3) };
        Grid.SetColumn(fill, 0);
        _track.Children.Add(fill);
    }

    /// <summary>The clamped fill fraction (for tests/consumers).</summary>
    public double FillFraction => Math.Clamp(Value, 0, 1);
}

/// <summary>One proportional segment of a <see cref="StackedBar"/>.</summary>
public sealed record BarSegment(double Fraction, IBrush Brush);

/// <summary>
/// SC-6 · Stacked bar (prototype <c>:267</c>): several proportional coloured segments (e.g. hot/warm/cold).
/// (16-checklist SC-6.)
/// </summary>
public class StackedBar : TemplatedControl
{
    private Grid? _track;

    public static readonly StyledProperty<IEnumerable?> SegmentsProperty =
        AvaloniaProperty.Register<StackedBar, IEnumerable?>(nameof(Segments));

    public IEnumerable? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Grid>("PART_Track");
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty)
            Apply();
    }

    private void Apply()
    {
        if (_track is null)
            return;
        _track.Children.Clear();
        var cols = new ColumnDefinitions();
        var i = 0;
        if (Segments is not null)
        {
            foreach (var s in Segments.OfType<BarSegment>())
            {
                cols.Add(new ColumnDefinition(new GridLength(Math.Max(0, s.Fraction), GridUnitType.Star)));
                var seg = new Border { Background = s.Brush };
                Grid.SetColumn(seg, i);
                _track.Children.Add(seg);
                i++;
            }
        }
        _track.ColumnDefinitions = cols;
    }

    /// <summary>Segment count (for tests).</summary>
    public int SegmentCount => Segments?.OfType<BarSegment>().Count() ?? 0;
}
