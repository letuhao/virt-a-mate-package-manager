using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// SC-1 · Theme tokens & palette. The prototype's :root vars are available as Avalonia resources in both
/// Light and Dark variants, and the two variants actually differ. (16-checklist SC-1.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ThemeTokensTests
{
    private static readonly string[] ColorTokens =
    [
        "Bg0Color", "Bg1Color", "Bg2Color", "Bg3Color", "BgHoverColor",
        "BorderColor", "BorderHiColor", "TextHiColor", "TextColor", "TextLoColor",
        "AccentColor", "AccentHiColor", "AccentBgColor", "AccentLineColor",
        "GoodColor", "GoodBgColor", "WarnColor", "WarnBgColor", "CritColor", "CritBgColor",
        "HotColor", "WarmColor", "ColdColor",
    ];

    private static readonly string[] BrushTokens =
    [
        "Bg1Brush", "AccentBrush", "TextHiBrush", "HotBrush", "WarmBrush", "ColdBrush",
        "GoodBrush", "WarnBrush", "CritBrush", "BorderBrush",
    ];

    [AvaloniaFact]
    public void Every_colour_token_resolves_in_light_and_dark()
    {
        var app = Application.Current!;
        foreach (var key in ColorTokens)
        {
            Assert.True(app.TryGetResource(key, ThemeVariant.Dark, out var dark) && dark is Color,
                $"{key} missing in Dark");
            Assert.True(app.TryGetResource(key, ThemeVariant.Light, out var light) && light is Color,
                $"{key} missing in Light");
        }
    }

    [AvaloniaFact]
    public void Every_brush_token_resolves()
    {
        var app = Application.Current!;
        foreach (var key in BrushTokens)
            Assert.True(app.TryGetResource(key, ThemeVariant.Dark, out var b) && b is ISolidColorBrush,
                $"{key} missing");
    }

    [AvaloniaFact]
    public void Light_and_dark_variants_actually_differ()
    {
        var app = Application.Current!;
        // Background + accent must not be the same across variants (proves both dictionaries are real).
        foreach (var key in new[] { "Bg1Color", "AccentColor", "TextHiColor" })
        {
            app.TryGetResource(key, ThemeVariant.Dark, out var dark);
            app.TryGetResource(key, ThemeVariant.Light, out var light);
            Assert.NotEqual((Color)dark!, (Color)light!);
        }
    }

    [AvaloniaFact]
    public void Non_colour_tokens_resolve()
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("RadiusMd", ThemeVariant.Default, out var r) && r is CornerRadius);
        Assert.True(app.TryGetResource("MonoFont", ThemeVariant.Default, out var f) && f is FontFamily);
        Assert.True(app.TryGetResource("FsBase", ThemeVariant.Default, out var s) && s is double);
    }
}
