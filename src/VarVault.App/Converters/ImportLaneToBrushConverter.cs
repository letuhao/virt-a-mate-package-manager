using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VarVault.Sdk.Import;

namespace VarVault.App.Converters;

/// <summary>
/// Maps an <see cref="ImportLane"/> to its lane accent brush so the Import list/gallery colour-codes each lane the
/// way the UX draft does (green=new, cold-blue=CJK, grey=exact, amber=conflict, purple=name≠meta, red=corrupt).
/// A fixed palette (independent of the theme accent) keeps the six lanes distinguishable at a glance. (draft §10.)
/// </summary>
public sealed class ImportLaneToBrushConverter : IValueConverter
{
    public static readonly ImportLaneToBrushConverter Instance = new();

    private static readonly IBrush New = Brush.Parse("#3ddc97");
    private static readonly IBrush Cjk = Brush.Parse("#6aa2c8");
    private static readonly IBrush Exact = Brush.Parse("#5b636e");
    private static readonly IBrush Conflict = Brush.Parse("#f4b740");
    private static readonly IBrush Naming = Brush.Parse("#c98bdb");
    private static readonly IBrush Corrupt = Brush.Parse("#fb6f84");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ImportLane.New => New,
        ImportLane.Cjk => Cjk,
        ImportLane.Exact => Exact,
        ImportLane.Conflict => Conflict,
        ImportLane.Naming => Naming,
        _ => Corrupt,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
