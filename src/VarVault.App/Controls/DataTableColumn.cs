using Avalonia;

namespace VarVault.App.Controls;

/// <summary>One column of a <see cref="DataTable"/> header: label, sort key, and alignment. (16-checklist SC-3.)</summary>
public class DataTableColumn : AvaloniaObject
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<DataTableColumn, string?>(nameof(Header));

    /// <summary>Value passed to the table's SortCommand when this header is clicked. Null = not sortable.</summary>
    public static readonly StyledProperty<object?> SortKeyProperty =
        AvaloniaProperty.Register<DataTableColumn, object?>(nameof(SortKey));

    /// <summary>Right-align the header (numeric column).</summary>
    public static readonly StyledProperty<bool> NumericProperty =
        AvaloniaProperty.Register<DataTableColumn, bool>(nameof(Numeric));

    public string? Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public object? SortKey { get => GetValue(SortKeyProperty); set => SetValue(SortKeyProperty, value); }
    public bool Numeric { get => GetValue(NumericProperty); set => SetValue(NumericProperty, value); }
}
