using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace VarVault.App.Controls;

/// <summary>State of a <see cref="StatePill"/> — always shown as a text label too (colourblind-safe).</summary>
public enum StateKind { Ok, Sub, Miss }

/// <summary>
/// SC-5 · State pill (prototype <c>.st</c> ok/sub/miss). Colour signals state but the <see cref="Text"/>
/// label always carries the meaning — never colour alone (HR-5). (16-checklist SC-5.)
/// </summary>
public class StatePill : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatePill, string?>(nameof(Text));

    public static readonly StyledProperty<StateKind> StateProperty =
        AvaloniaProperty.Register<StatePill, StateKind>(nameof(State));

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public StateKind State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }

    public StatePill() => ApplyStateClass();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
            ApplyStateClass();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (string.IsNullOrEmpty(Text)) // default the label to the state name (still text, not colour)
            Text = State.ToString().ToLowerInvariant();
    }

    private void ApplyStateClass()
    {
        Classes.Remove("ok"); Classes.Remove("sub"); Classes.Remove("miss");
        Classes.Add(State switch { StateKind.Ok => "ok", StateKind.Sub => "sub", _ => "miss" });
    }
}

/// <summary>SC-5 · Filter chip (prototype <c>.chip</c>); <see cref="IsActive"/> = selected. (16-checklist SC-5.)</summary>
public class Chip : ContentControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<Chip, bool>(nameof(IsActive));

    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            if (IsActive) Classes.Add("active"); else Classes.Remove("active");
        }
    }
}

/// <summary>Kind of a <see cref="Tag"/> — neutral or semantic (good/warn/crit).</summary>
public enum TagKind { Default, Good, Warn, Crit }

/// <summary>SC-5 · Tag (prototype <c>.tag</c> good/warn/crit). (16-checklist SC-5.)</summary>
public class Tag : ContentControl
{
    public static readonly StyledProperty<TagKind> KindProperty =
        AvaloniaProperty.Register<Tag, TagKind>(nameof(Kind));

    public TagKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public Tag() => ApplyKind();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
            ApplyKind();
    }

    private void ApplyKind()
    {
        Classes.Remove("good"); Classes.Remove("warn"); Classes.Remove("crit");
        switch (Kind)
        {
            case TagKind.Good: Classes.Add("good"); break;
            case TagKind.Warn: Classes.Add("warn"); break;
            case TagKind.Crit: Classes.Add("crit"); break;
        }
    }
}
