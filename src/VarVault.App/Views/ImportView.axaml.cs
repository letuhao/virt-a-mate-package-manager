using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>Import &amp; review screen. Handles the review keyboard map (J/K, ] [ \, Del). (doc 31 Phase 6.2/6.6.)</summary>
public partial class ImportView : UserControl
{
    private ListBox? _itemList;
    private ListBox? _galleryList;

    public ImportView()
    {
        AvaloniaXamlLoader.Load(this);
        _itemList = this.FindControl<ListBox>("ItemList");
        _galleryList = this.FindControl<ListBox>("GalleryList");
        // Tunnel: J/K + decision keys win before ListBox type-ahead / Del. (6.6)
        // Note: ListBox.OnKeyDown is a class handler and still runs for ↑/↓ even when Handled —
        // so arrows are owned by ListBox (native move+scroll); we only block XY-focus escape in bubble.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnArrowBubble, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => WirePickers();
        AttachedToVisualTree += (_, _) => WirePickers();
        // DataContext may already be set by a DataTemplate before our handler was subscribed.
        WirePickers();
    }

    /// <summary>Wire the native OS folder/archive pickers (Avalonia StorageProvider — no extra library). (QoL)</summary>
    private void WirePickers()
    {
        if (DataContext is not ImportViewModel vm)
            return;
        // Always reassign: the view may be rebound to a fresh VM after navigation.
        vm.FolderPicker = PickFoldersAsync;
        vm.ArchivePicker = PickArchivesAsync;
    }

    private async Task<IReadOnlyList<string>> PickFoldersAsync()
    {
        var top = await ResolveTopLevelAsync().ConfigureAwait(true);
        if (top is null)
        {
            if (DataContext is ImportViewModel vm)
                vm.StatusMessage = "Can't open the folder picker — window not ready. Try again.";
            return [];
        }
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folders containing .var files",
            AllowMultiple = true,
        }).ConfigureAwait(true);
        return folders
            .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();
    }

    private async Task<IReadOnlyList<string>> PickArchivesAsync()
    {
        var top = await ResolveTopLevelAsync().ConfigureAwait(true);
        if (top is null)
        {
            if (DataContext is ImportViewModel vm)
                vm.StatusMessage = "Can't open the archive picker — window not ready. Try again.";
            return [];
        }
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select archives (zip / 7z / rar / tar)",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Archives") { Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.gz"] },
                FilePickerFileTypes.All,
            ],
        }).ConfigureAwait(true);
        return files
            .Select(f => f.TryGetLocalPath() ?? f.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();
    }

    /// <summary>
    /// TopLevel can be null briefly after DataContext is set but before the control is attached.
    /// Wait one layout pass so the native picker has a host window.
    /// </summary>
    private async Task<TopLevel?> ResolveTopLevelAsync()
    {
        var top = TopLevel.GetTopLevel(this) ?? this.GetVisualRoot() as TopLevel;
        if (top is not null)
            return top;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        return TopLevel.GetTopLevel(this) ?? this.GetVisualRoot() as TopLevel;
    }

    /// <summary>
    /// Review shortcuts: J/K navigate (same as ↑/↓), ] [ \ decide, Del discard.
    /// ↑/↓ are intentionally not moved here — ListBox class handlers ignore Handled and would double-step.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ImportViewModel vm)
            return;
        if (e.Source is TextBox or ComboBox)   // don't hijack typing/filtering
            return;

        switch (e.Key)
        {
            case Key.J:
                vm.MoveSelection(+1);
                KeepSelectionVisible();
                break;
            case Key.K:
                vm.MoveSelection(-1);
                KeepSelectionVisible();
                break;
            case Key.OemCloseBrackets: vm.DecidePrimary(); break;   // ]
            case Key.OemOpenBrackets: vm.DecideSecondary(); break;  // [
            case Key.OemPipe or Key.OemBackslash: vm.DecideBoth(); break; // \
            case Key.Delete or Key.Back: vm.DiscardSelected(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// After ListBox handles ↑/↓ (or fails at the edge), mark Handled so Window XY-focus cannot jump to
    /// Details / Pagination, and keep the selected row scrolled + focused inside the list.
    /// </summary>
    private void OnArrowBubble(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ImportViewModel)
            return;
        if (e.Source is TextBox or ComboBox)
            return;
        if (e.Key is not (Key.Up or Key.Down))
            return;

        // If focus isn't in either list, drive selection ourselves (e.g. focus landed on a Details button).
        if (!IsFocusInsideList() && !e.Handled)
        {
            if (DataContext is ImportViewModel vm)
                vm.MoveSelection(e.Key == Key.Down ? +1 : -1);
        }

        e.Handled = true;
        KeepSelectionVisible();
    }

    private bool IsFocusInsideList()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null)
            return false;
        return (_itemList is not null && _itemList.IsVisualAncestorOf(focused))
            || (_galleryList is not null && _galleryList.IsVisualAncestorOf(focused))
            || ReferenceEquals(focused, _itemList)
            || ReferenceEquals(focused, _galleryList);
    }

    /// <summary>
    /// Scroll the selected row into view and reclaim keyboard focus so navigation stays in the list.
    /// </summary>
    private void KeepSelectionVisible()
    {
        if (DataContext is not ImportViewModel { Selected: { } selected })
            return;

        var list = ActiveList();
        if (list is null)
            return;

        // Defer until after SelectedItem binding + container generation (virtualized panel).
        Dispatcher.UIThread.Post(() =>
        {
            list.ScrollIntoView(selected);
            if (list.ContainerFromItem(selected) is Control container)
                container.Focus(NavigationMethod.Directional);
            else
                list.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Render);
    }

    private ListBox? ActiveList()
    {
        if (DataContext is ImportViewModel { GalleryView: true })
            return _galleryList is { IsVisible: true } ? _galleryList : _itemList;
        return _itemList is { IsVisible: true } ? _itemList : _galleryList;
    }
}
