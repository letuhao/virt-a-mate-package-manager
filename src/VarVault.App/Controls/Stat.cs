using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace VarVault.App.Controls;

/// <summary>
/// SC-7 · Stat tile (prototype <c>.stat</c>): a big number with a small unit label (e.g. "590" · "GB
/// recoverable"). (16-checklist SC-7.)
/// </summary>
public class StatTile : TemplatedControl
{
    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Value));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<StatTile, string?>(nameof(Unit));

    public static readonly StyledProperty<IBrush?> ValueBrushProperty =
        AvaloniaProperty.Register<StatTile, IBrush?>(nameof(ValueBrush));

    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string? Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public IBrush? ValueBrush { get => GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
}

/// <summary>Storage temperature — mirrors the domain content class.</summary>
public enum Temperature { Hot, Warm, Cold }

/// <summary>
/// SC-7 · Temperature dot (prototype <c>.temp</c>): an 8px colour dot for hot/warm/cold. Always paired with
/// a text label by the consumer (colour is supplementary, not the sole signal). (16-checklist SC-7.)
/// </summary>
public class TempDot : TemplatedControl
{
    public static readonly StyledProperty<Temperature> TemperatureProperty =
        AvaloniaProperty.Register<TempDot, Temperature>(nameof(Temperature));

    public Temperature Temperature { get => GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }

    public TempDot() => ApplyClass();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TemperatureProperty)
            ApplyClass();
    }

    private void ApplyClass()
    {
        Classes.Remove("hot"); Classes.Remove("warm"); Classes.Remove("cold");
        Classes.Add(Temperature switch { Temperature.Hot => "hot", Temperature.Warm => "warm", _ => "cold" });
    }
}
