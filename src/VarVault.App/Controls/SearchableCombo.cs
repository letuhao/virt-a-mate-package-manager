using System.Collections;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace VarVault.App.Controls;

/// <summary>A combo option: display name + count (e.g. "MeshedVR" · 412). (16-checklist SC-8.)</summary>
public sealed record ComboOption(string Name, int Count);

/// <summary>
/// SC-8 · Searchable combo (prototype creator dropdown): a button (label + caret) opening a panel with a
/// search box, a count-annotated filtered list, keyboard ↑/↓/Enter/Esc, and an empty state. Type-to-filter
/// scales to tens of thousands of options (client filter). The filter/keyboard logic is exposed as methods
/// so it is testable without a live popup. (16-checklist SC-8.)
/// </summary>
public class SearchableCombo : TemplatedControl
{
    private TextBox? _search;

    public SearchableCombo() => FilteredOptions = new AvaloniaList<ComboOption>();

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableCombo, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> FilterTextProperty =
        AvaloniaProperty.Register<SearchableCombo, string?>(nameof(FilterText));

    public static readonly StyledProperty<string?> SelectedNameProperty =
        AvaloniaProperty.Register<SearchableCombo, string?>(nameof(SelectedName), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<SearchableCombo, bool>(nameof(IsOpen));

    public static readonly StyledProperty<int> HighlightedIndexProperty =
        AvaloniaProperty.Register<SearchableCombo, int>(nameof(HighlightedIndex));

    public static readonly DirectProperty<SearchableCombo, AvaloniaList<ComboOption>> FilteredOptionsProperty =
        AvaloniaProperty.RegisterDirect<SearchableCombo, AvaloniaList<ComboOption>>(nameof(FilteredOptions), o => o.FilteredOptions);

    public static readonly DirectProperty<SearchableCombo, bool> IsEmptyProperty =
        AvaloniaProperty.RegisterDirect<SearchableCombo, bool>(nameof(IsEmpty), o => o.IsEmpty);

    private AvaloniaList<ComboOption> _filteredOptions = new();
    private bool _isEmpty;

    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? FilterText { get => GetValue(FilterTextProperty); set => SetValue(FilterTextProperty, value); }
    public string? SelectedName { get => GetValue(SelectedNameProperty); set => SetValue(SelectedNameProperty, value); }
    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public int HighlightedIndex { get => GetValue(HighlightedIndexProperty); set => SetValue(HighlightedIndexProperty, value); }
    public AvaloniaList<ComboOption> FilteredOptions { get => _filteredOptions; private set => SetAndRaise(FilteredOptionsProperty, ref _filteredOptions, value); }
    public bool IsEmpty { get => _isEmpty; private set => SetAndRaise(IsEmptyProperty, ref _isEmpty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _search = e.NameScope.Find<TextBox>("PART_Search");
        if (_search is not null)
            _search.KeyDown += OnSearchKeyDown;
        if (e.NameScope.Find<Button>("PART_Button") is { } button)
            button.Click += (_, _) => Open();
        Refilter();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilterTextProperty || change.Property == ItemsSourceProperty)
            Refilter();
    }

    /// <summary>Rebuild the filtered list from the current filter text (Contains, case-insensitive).</summary>
    public void Refilter()
    {
        var all = ItemsSource?.OfType<ComboOption>() ?? [];
        var f = FilterText;
        var matches = string.IsNullOrWhiteSpace(f)
            ? all.ToList()
            : all.Where(o => o.Name.Contains(f!, StringComparison.OrdinalIgnoreCase)).ToList();
        FilteredOptions.Clear();
        FilteredOptions.AddRange(matches);
        IsEmpty = matches.Count == 0;
        HighlightedIndex = matches.Count > 0 ? 0 : -1;
    }

    public void Open()
    {
        IsOpen = true;
        FilterText = string.Empty;
        Refilter();
    }

    public void Close() => IsOpen = false;

    /// <summary>Move the keyboard highlight, clamped to the filtered list.</summary>
    public void MoveHighlight(int delta)
    {
        if (FilteredOptions.Count == 0) { HighlightedIndex = -1; return; }
        HighlightedIndex = Math.Clamp(HighlightedIndex + delta, 0, FilteredOptions.Count - 1);
    }

    /// <summary>Commit the highlighted option as the selection and close.</summary>
    public void CommitHighlighted()
    {
        if (HighlightedIndex >= 0 && HighlightedIndex < FilteredOptions.Count)
            SelectedName = FilteredOptions[HighlightedIndex].Name;
        Close();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key))
            e.Handled = true;
    }

    /// <summary>The keyboard map (↑/↓ highlight, Enter picks, Esc closes). Returns true if handled.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Down: MoveHighlight(1); return true;
            case Key.Up: MoveHighlight(-1); return true;
            case Key.Enter: CommitHighlighted(); return true;
            case Key.Escape: Close(); return true;
            default: return false;
        }
    }
}
