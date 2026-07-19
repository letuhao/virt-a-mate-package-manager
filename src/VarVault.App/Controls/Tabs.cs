using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace VarVault.App.Controls;

/// <summary>A tab label with an optional count badge (e.g. "Encoding" · 684). (16-checklist SC-4.)</summary>
public sealed record TabItemModel(string Label, int? Count = null);

/// <summary>
/// SC-4 · Tabs strip (prototype <c>.tabs</c>): a row of tab buttons, one active, with optional count
/// badges. Exposes <see cref="SelectedIndex"/> / <see cref="SelectedItem"/> so a screen VM can react and
/// swap content (tabs drive server-side content, not embedded panels). (16-checklist SC-4.)
/// </summary>
public class Tabs : TemplatedControl
{
    private StackPanel? _strip;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<Tabs, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<Tabs, int>(nameof(SelectedIndex), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly DirectProperty<Tabs, object?> SelectedItemProperty =
        AvaloniaProperty.RegisterDirect<Tabs, object?>(nameof(SelectedItem), o => o.SelectedItem);

    private object? _selectedItem;

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public object? SelectedItem { get => _selectedItem; private set => SetAndRaise(SelectedItemProperty, ref _selectedItem, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _strip = e.NameScope.Find<StackPanel>("PART_Strip");
        Build();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
            Build();
        else if (change.Property == SelectedIndexProperty)
            UpdateActive();
    }

    private void Build()
    {
        if (_strip is null)
            return;
        _strip.Children.Clear();
        if (ItemsSource is null)
            return;

        var i = 0;
        foreach (var item in ItemsSource)
        {
            var index = i;
            var label = item is TabItemModel m ? m.Label : item?.ToString() ?? "";
            var count = (item as TabItemModel)?.Count;

            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            if (count is { } c)
            {
                content.Children.Add(new Border
                {
                    Background = GetBrush("Bg3Brush"),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 0),
                    Child = new TextBlock { Text = c.ToString(), FontSize = 10, Opacity = 0.8 },
                });
            }

            var btn = new Button
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(10, 6),
                Tag = item,
            };
            btn.Click += (_, _) => SelectedIndex = index;
            _strip.Children.Add(btn);
            i++;
        }
        UpdateActive();
    }

    private void UpdateActive()
    {
        if (_strip is null)
            return;
        var idx = SelectedIndex;
        for (var i = 0; i < _strip.Children.Count; i++)
        {
            if (_strip.Children[i] is Button b)
            {
                var active = i == idx;
                b.Foreground = active ? GetBrush("AccentHiBrush") : GetBrush("TextBrush");
                b.BorderBrush = active ? GetBrush("AccentBrush") : Brushes.Transparent;
                if (active)
                    b.Classes.Add("active");
                else
                    b.Classes.Remove("active");
            }
        }
        SelectedItem = idx >= 0 && _strip.Children.Count > idx && _strip.Children[idx] is Button ab ? ab.Tag : null;
    }

    private IBrush? GetBrush(string key) =>
        this.TryFindResource(key, out var v) && v is IBrush b ? b : null;
}
