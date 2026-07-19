using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using VarVault.App.Controls;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// SC-3 · DataTable: sortable header raises the sort command, the active caret reflects sort state, the
/// body virtualizes a large list, and multi-select populates SelectedItems. (16-checklist SC-3.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class DataTableControlTests
{
    private static DataTable BuildTable(out object? sortedBy, out List<string> items)
    {
        var captured = new object?[1];
        var list = Enumerable.Range(0, 5000).Select(i => $"row{i}").ToList();
        items = list;

        var table = new DataTable
        {
            ColumnWidths = "40,*,90",
            ItemsSource = list,
            SelectedItems = new AvaloniaList<object>(),
            SortCommand = new RelayCommand<object?>(k => captured[0] = k),
            RowTemplate = new FuncDataTemplate<string>((s, _) => new TextBlock { Text = s, Height = 22 }),
        };
        table.Columns.Add(new DataTableColumn { Header = "✓" });                       // non-sortable
        table.Columns.Add(new DataTableColumn { Header = "Name", SortKey = "name" });
        table.Columns.Add(new DataTableColumn { Header = "Size", SortKey = "size", Numeric = true });

        // expose captured via a closure through the out param after interaction
        sortedBy = null;
        _lastCaptured = captured;
        return table;
    }

    private static object?[] _lastCaptured = new object?[1];

    [AvaloniaFact]
    public void Header_click_raises_sort_command_with_the_column_key()
    {
        var table = BuildTable(out _, out _);
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var sizeHeader = table.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "size"));
        sizeHeader.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal("size", _lastCaptured[0]);
    }

    [AvaloniaFact]
    public void Active_caret_reflects_sort_key_and_direction()
    {
        var table = BuildTable(out _, out _);
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        table.SortKey = "name";
        table.SortDescending = false;
        Dispatcher.UIThread.RunJobs();

        var nameBtn = table.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "name"));
        var sizeBtn = table.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "size"));
        var nameCaret = nameBtn.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is "▲" or "▼" or "▲▼");
        var sizeCaret = sizeBtn.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text is "▲" or "▼" or "▲▼");

        Assert.Equal("▲", nameCaret.Text);   // active, ascending
        Assert.Equal("▲▼", sizeCaret.Text);   // inactive
    }

    [AvaloniaFact]
    public void Body_virtualizes_a_large_list()
    {
        var table = BuildTable(out _, out _);
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var realized = table.GetVisualDescendants().OfType<ListBoxItem>().Count();
        Assert.True(realized > 0 && realized < 200, $"expected virtualization, realized {realized} of 5000");
    }

    [AvaloniaFact]
    public void Multi_select_populates_selected_items()
    {
        var table = BuildTable(out _, out var items);
        var window = new Window { Width = 600, Height = 400, Content = table };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var rows = table.GetVisualDescendants().OfType<ListBox>().First();
        rows.SelectedItems!.Add(items[0]);
        rows.SelectedItems.Add(items[1]);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, table.SelectedItems!.Count);
    }
}
