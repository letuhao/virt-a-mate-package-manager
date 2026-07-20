using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace VarVault.App.Converters;

/// <summary>
/// Binds an absolute image-file path (a string) to a decoded <see cref="Bitmap"/> — used by the Import gallery/resolver
/// to show previews extracted out of incoming vars into the session temp (they aren't catalogued, so
/// <c>IThumbnailStore</c> doesn't have them yet). Null/missing/undecodable paths render nothing so the card falls back
/// to its placeholder. Decoded bitmaps are memoized by path so re-templating a scrolled list doesn't re-decode.
/// (doc 31 Phase 6.3.)
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    private readonly ConcurrentDictionary<string, Bitmap?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;
        return _cache.GetOrAdd(path, Decode);
    }

    private static Bitmap? Decode(string path)
    {
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
