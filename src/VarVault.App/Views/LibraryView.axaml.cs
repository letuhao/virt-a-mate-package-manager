using System.Collections.Specialized;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>SCR-2 · Library screen with DataGrid, resizable detail panel, and keyboard shortcuts.</summary>
public partial class LibraryView : UserControl
{
    private ColumnDefinition? _detailColumn;
    private bool _suppressWidthSync;
    private DispatcherTimer? _saveWidthTimer;
    private DispatcherTimer? _saveColumnsTimer;
    private DataGrid? _packageGrid;
    private LibraryViewModel? _subscribedViewModel;
    private bool _syncingSelection;
    private bool _applyingColumns;

    public LibraryView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged, RoutingStrategies.Bubble);
        DataContextChanged += OnDataContextChanged;
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var root = this.FindControl<Grid>("RootGrid");
        _packageGrid = this.FindControl<DataGrid>("PackageGrid");
        _detailColumn = root?.ColumnDefinitions.Count == 4 ? root.ColumnDefinitions[3] : null;
        ApplyDetailWidthFromVm();
        if (_detailColumn is not null)
            _detailColumn.PropertyChanged += OnDetailColumnPropertyChanged;
        SubscribeViewModel(DataContext as LibraryViewModel);
        if (_packageGrid is not null)
        {
            foreach (var column in _packageGrid.Columns)
                column.PropertyChanged += OnGridColumnPropertyChanged;
            if (DataContext is LibraryViewModel vm)
                ApplyColumnLayout(await vm.LoadColumnLayoutAsync());
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        if (_detailColumn is not null)
            _detailColumn.PropertyChanged -= OnDetailColumnPropertyChanged;
        if (_packageGrid is not null)
            foreach (var column in _packageGrid.Columns)
                column.PropertyChanged -= OnGridColumnPropertyChanged;
        SubscribeViewModel(null);
        _saveWidthTimer?.Stop();
        _saveColumnsTimer?.Stop();
        base.OnUnloaded(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        ApplyDetailWidthFromVm();
        SubscribeViewModel(DataContext as LibraryViewModel);
    }

    private void SubscribeViewModel(LibraryViewModel? vm)
    {
        if (_subscribedViewModel is not null)
            _subscribedViewModel.SelectedItems.CollectionChanged -= OnViewModelSelectionChanged;
        _subscribedViewModel = vm;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.SelectedItems.CollectionChanged += OnViewModelSelectionChanged;
    }

    private void ApplyDetailWidthFromVm()
    {
        if (_detailColumn is null || DataContext is not LibraryViewModel vm)
            return;
        _suppressWidthSync = true;
        _detailColumn.Width = new GridLength(Math.Max(PackageGalleryViewModel.MinPanelWidth, vm.DetailPanelWidth));
        _suppressWidthSync = false;
    }

    private void OnDetailColumnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (_suppressWidthSync || e.Property != ColumnDefinition.WidthProperty || DataContext is not LibraryViewModel vm)
            return;
        if (_detailColumn is null || !_detailColumn.Width.IsAbsolute)
            return;
        var width = Math.Max(PackageGalleryViewModel.MinPanelWidth, _detailColumn.Width.Value);
        if (Math.Abs(vm.DetailPanelWidth - width) < 0.5)
            return;
        vm.DetailPanelWidth = width;
        _saveWidthTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveWidthTimer.Stop();
        _saveWidthTimer.Tick -= SaveWidthTick;
        _saveWidthTimer.Tick += SaveWidthTick;
        _saveWidthTimer.Start();
    }

    private async void SaveWidthTick(object? sender, EventArgs e)
    {
        _saveWidthTimer?.Stop();
        if (DataContext is LibraryViewModel vm)
            await vm.SaveDetailWidthAsync();
    }

    private void OnGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not DataGrid grid || DataContext is not LibraryViewModel vm)
            return;
        _syncingSelection = true;
        try
        {
            // Sync only loaded rows so select-all-matching IDs beyond the page survive.
            var gridSelected = grid.SelectedItems.OfType<PackageListEntry>().Select(x => x.PackageId).ToHashSet();
            foreach (var entry in vm.Items)
                vm.SetSelected(entry.PackageId, gridSelected.Contains(entry.PackageId));
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnViewModelSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_syncingSelection || _packageGrid is null || DataContext is not LibraryViewModel vm)
            return;
        _syncingSelection = true;
        try
        {
            _packageGrid.SelectedItems.Clear();
            foreach (var entry in vm.SelectedItems)
                _packageGrid.SelectedItems.Add(entry);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private async void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm)
            return;
        var sort = e.Column.SortMemberPath switch
        {
            "Name" => LibrarySort.Name,
            "Creator" => LibrarySort.Creator,
            "Size" => LibrarySort.Size,
            "Tier" => LibrarySort.Class,
            "Added" => LibrarySort.Added,
            "Installed" => LibrarySort.Installed,
            _ => (LibrarySort?)null,
        };
        if (sort is null)
            return;
        e.Handled = true;
        await vm.SortByAsync(sort.Value);
    }

    private void OnGridColumnReordered(object? sender, DataGridColumnEventArgs e) => ScheduleColumnSave();

    private void OnGridColumnPropertyChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (!_applyingColumns && (e.Property.Name == "Width" || e.Property.Name == "IsVisible" || e.Property.Name == "DisplayIndex"))
            ScheduleColumnSave();
    }

    private void OnColumnVisibilityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not string id || _packageGrid is null)
            return;
        var column = _packageGrid.Columns.FirstOrDefault(c => c.SortMemberPath == id);
        if (column is null)
            return;
        column.IsVisible = item.IsChecked;
        ScheduleColumnSave();
    }

    private void ScheduleColumnSave()
    {
        if (_applyingColumns)
            return;
        _saveColumnsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveColumnsTimer.Stop();
        _saveColumnsTimer.Tick -= SaveColumnsTick;
        _saveColumnsTimer.Tick += SaveColumnsTick;
        _saveColumnsTimer.Start();
    }

    private async void SaveColumnsTick(object? sender, EventArgs e)
    {
        _saveColumnsTimer?.Stop();
        if (_packageGrid is null || DataContext is not LibraryViewModel vm)
            return;
        var layout = _packageGrid.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.SortMemberPath))
            .Select(c => new ColumnLayout(c.SortMemberPath, c.ActualWidth, c.DisplayIndex, c.IsVisible))
            .ToList();
        await vm.SaveColumnLayoutAsync(JsonSerializer.Serialize(layout));
    }

    private void ApplyColumnLayout(string? json)
    {
        if (_packageGrid is null || string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            var layout = JsonSerializer.Deserialize<List<ColumnLayout>>(json);
            if (layout is null)
                return;
            _applyingColumns = true;
            foreach (var saved in layout.OrderBy(x => x.DisplayIndex))
            {
                var column = _packageGrid.Columns.FirstOrDefault(c => c.SortMemberPath == saved.Id);
                if (column is null)
                    continue;
                if (saved.Width > 20)
                    column.Width = new DataGridLength(saved.Width);
                column.IsVisible = saved.IsVisible;
                column.DisplayIndex = Math.Clamp(saved.DisplayIndex, 0, _packageGrid.Columns.Count - 1);
            }
        }
        catch (JsonException)
        {
            // A versioned/corrupt layout falls back to the declared defaults.
        }
        finally
        {
            _applyingColumns = false;
        }
    }

    private void OnGalleryCardAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: GalleryCardViewModel card })
            _ = card.LoadAsync();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer viewer || DataContext is not LibraryViewModel vm ||
            !vm.HasMore || vm.IsLoadingMore)
            return;
        if (viewer.Offset.Y + viewer.Viewport.Height >= viewer.Extent.Height - 240)
            _ = vm.LoadMoreAsync();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm)
            return;
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.D && e.KeyModifiers.HasFlag(KeyModifiers.Control) && vm.SelectedEntry is not null)
        {
            vm.OpenDetailCommand.Execute(vm.SelectedEntry);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && vm.SelectedEntry is not null)
        {
            _ = vm.ToggleFavoriteAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.PackageGallery.IsFocusMode)
            {
                vm.PackageGallery.ExitFocusCommand.Execute(null);
                e.Handled = true;
                return;
            }
            vm.ClearSelection();
            vm.SelectedEntry = null;
            e.Handled = true;
        }
        else if (vm.PackageGallery.IsFocusMode)
        {
            // Focus lives in the sidebar; grid often has keyboard focus, so route nav here.
            switch (e.Key)
            {
                case Key.Left:
                    _ = vm.PackageGallery.FocusPreviousCommand.ExecuteAsync(null);
                    e.Handled = true;
                    break;
                case Key.Right:
                    _ = vm.PackageGallery.FocusNextCommand.ExecuteAsync(null);
                    e.Handled = true;
                    break;
                case Key.Home:
                    _ = vm.PackageGallery.FocusFirstCommand.ExecuteAsync(null);
                    e.Handled = true;
                    break;
                case Key.End:
                    _ = vm.PackageGallery.FocusLastCommand.ExecuteAsync(null);
                    e.Handled = true;
                    break;
            }
        }
    }

    private sealed record ColumnLayout(string Id, double Width, int DisplayIndex, bool IsVisible);
}
