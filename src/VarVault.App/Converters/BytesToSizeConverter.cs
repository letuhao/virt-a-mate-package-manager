using System;
using System.Globalization;
using Avalonia.Data.Converters;
using VarVault.Common.Formatting;

namespace VarVault.App.Converters;

/// <summary>
/// Binds a raw byte count (<see cref="long"/>/<see cref="int"/>/<see cref="double"/>) to a humanized size string
/// via <see cref="ByteSize.Humanize"/> — so Dashboard / Duplicates / Library-grid stop showing unreadable
/// digit-only byte values. Non-numeric or null input renders empty. (28-checklist A1.2.)
/// </summary>
public sealed class BytesToSizeConverter : IValueConverter
{
    public static readonly BytesToSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        long l => ByteSize.Humanize(l),
        int i => ByteSize.Humanize(i),
        double d => ByteSize.Humanize((long)d),
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
