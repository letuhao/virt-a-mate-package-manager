using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace VarVault.App.Controls;

/// <summary>
/// SC-3 · Reusable data table for server-paged data: a sortable header band + a virtualized multi-select
/// body. Header clicks raise <see cref="SortCommand"/> (server-side sort, not client sort — the body may
/// hold 70k virtualized rows); the active column's caret reflects <see cref="SortKey"/> /
/// <see cref="SortDescending"/>. Rows come from the consumer's <see cref="RowTemplate"/>, whose cell widths
/// must match <see cref="ColumnWidths"/>. (16-checklist SC-3.)
/// </summary>
public class DataTable : TemplatedControl
{
    private Grid? _header;
    private ListBox? _rows;

    public DataTable()
    {
        Columns = new AvaloniaList<DataTableColumn>();
    }

    public static readonly StyledProperty<AvaloniaList<DataTableColumn>> ColumnsProperty =
        AvaloniaProperty.Register<DataTable, AvaloniaList<DataTableColumn>>(nameof(Columns));

    /// <summary>Grid column widths shared by the header and each row (e.g. "Auto,*,160,90"). </summary>
    public static readonly StyledProperty<string> ColumnWidthsProperty =
        AvaloniaProperty.Register<DataTable, string>(nameof(ColumnWidths), "*");

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DataTable, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<DataTable, IDataTemplate?>(nameof(RowTemplate));

    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<DataTable, IList?>(nameof(SelectedItems));

    public static readonly StyledProperty<ICommand?> SortCommandProperty =
        AvaloniaProperty.Register<DataTable, ICommand?>(nameof(SortCommand));

    public static readonly StyledProperty<object?> SortKeyProperty =
        AvaloniaProperty.Register<DataTable, object?>(nameof(SortKey));

    public static readonly StyledProperty<bool> SortDescendingProperty =
        AvaloniaProperty.Register<DataTable, bool>(nameof(SortDescending));

    public AvaloniaList<DataTableColumn> Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public string ColumnWidths { get => GetValue(ColumnWidthsProperty); set => SetValue(ColumnWidthsProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public IDataTemplate? RowTemplate { get => GetValue(RowTemplateProperty); set => SetValue(RowTemplateProperty, value); }
    public IList? SelectedItems { get => GetValue(SelectedItemsProperty); set => SetValue(SelectedItemsProperty, value); }
    public ICommand? SortCommand { get => GetValue(SortCommandProperty); set => SetValue(SortCommandProperty, value); }
    public object? SortKey { get => GetValue(SortKeyProperty); set => SetValue(SortKeyProperty, value); }
    public bool SortDescending { get => GetValue(SortDescendingProperty); set => SetValue(SortDescendingProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _header = e.NameScope.Find<Grid>("PART_Header");
        _rows = e.NameScope.Find<ListBox>("PART_Rows");
        BuildHeader();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty || change.Property == ColumnWidthsProperty)
            BuildHeader();
        else if (change.Property == SortKeyProperty || change.Property == SortDescendingProperty)
            UpdateCarets();
    }

    private void BuildHeader()
    {
        if (_header is null)
            return;
        _header.Children.Clear();
        _header.ColumnDefinitions = new ColumnDefinitions(ColumnWidths);

        for (var i = 0; i < Columns.Count; i++)
        {
            var col = Columns[i];
            Control cell;
            if (col.SortKey is null)
            {
                cell = new TextBlock
                {
                    Text = col.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.7,
                    HorizontalAlignment = col.Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
            }
            else
            {
                var caret = new TextBlock { Text = "▲▼", FontSize = 9, Opacity = 0.5, Margin = new Thickness(4, 0, 0, 0), Tag = col.SortKey };
                var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = col.Header }, caret } };
                var btn = new Button
                {
                    Content = content,
                    Tag = col.SortKey,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = col.Numeric ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    Background = Brushes.Transparent,
                    BorderThickness = default,
                };
                btn.Click += (_, _) => { if (SortCommand?.CanExecute(col.SortKey) == true) SortCommand.Execute(col.SortKey); };
                cell = btn;
            }
            Grid.SetColumn(cell, i);
            _header.Children.Add(cell);
        }
        UpdateCarets();
    }

    private void UpdateCarets()
    {
        if (_header is null)
            return;
        foreach (var child in _header.Children)
        {
            if (child is Button btn && btn.Content is StackPanel sp && sp.Children.Count == 2 && sp.Children[1] is TextBlock caret)
            {
                var active = SortKey is not null && Equals(btn.Tag, SortKey);
                caret.Text = active ? (SortDescending ? "▼" : "▲") : "▲▼";
                caret.Opacity = active ? 1.0 : 0.5;
            }
        }
    }
}
