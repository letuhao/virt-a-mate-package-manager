using Avalonia;
using Avalonia.Controls;

namespace VarVault.App.Controls;

/// <summary>
/// SC-2 · Shared card container (prototype <c>.card</c>): bg-1 surface, border, radius, 14px padding, with
/// an optional <see cref="Header"/> title (h3). Content goes in the body. (16-checklist SC-2.)
/// </summary>
public class Card : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<Card, string?>(nameof(Header));

    /// <summary>Optional card title rendered as an h3 above the content.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
