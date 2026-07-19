using Avalonia;
using Avalonia.Controls;

namespace VarVault.App.Controls;

/// <summary>Badge emphasis for a rail item (neutral count vs a warning count).</summary>
public enum BadgeKind { Normal, Warn }

/// <summary>
/// SC-11 · Rail nav item (prototype <c>.rnav</c>): icon slot + label + optional count badge + active state.
/// (16-checklist SC-11.)
/// </summary>
public class RailNavItem : ContentControl // Content = icon
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<RailNavItem, string?>(nameof(Label));

    public static readonly StyledProperty<int?> BadgeProperty =
        AvaloniaProperty.Register<RailNavItem, int?>(nameof(Badge));

    public static readonly StyledProperty<BadgeKind> BadgeKindProperty =
        AvaloniaProperty.Register<RailNavItem, BadgeKind>(nameof(BadgeKind));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<RailNavItem, bool>(nameof(IsActive));

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public int? Badge { get => GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public BadgeKind BadgeKind { get => GetValue(BadgeKindProperty); set => SetValue(BadgeKindProperty, value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    /// <summary>True when a badge count is present (drives badge visibility).</summary>
    public bool HasBadge => Badge.HasValue;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            if (IsActive) Classes.Add("active"); else Classes.Remove("active");
        }
        else if (change.Property == BadgeKindProperty)
        {
            Classes.Remove("warn");
            if (BadgeKind == BadgeKind.Warn) Classes.Add("warn");
        }
        else if (change.Property == BadgeProperty)
        {
            RaisePropertyChanged(HasBadgeProperty, !HasBadge, HasBadge);
        }
    }

    public static readonly DirectProperty<RailNavItem, bool> HasBadgeProperty =
        AvaloniaProperty.RegisterDirect<RailNavItem, bool>(nameof(HasBadge), o => o.HasBadge);
}
