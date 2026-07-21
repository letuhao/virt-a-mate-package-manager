using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;

namespace VarVault.App.Views;

/// <summary>Import &amp; review screen. Handles the review keyboard map (J/K, ] [ \, Del). (doc 31 Phase 6.2/6.6.)</summary>
public partial class ImportView : UserControl
{
    public ImportView()
    {
        AvaloniaXamlLoader.Load(this);
        // Tunnel so the mapped keys win before the ListBox's type-ahead search / Del handling. (6.6)
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
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
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Loaded);
        return TopLevel.GetTopLevel(this) ?? this.GetVisualRoot() as TopLevel;
    }

    /// <summary>
    /// Review shortcuts from the UX draft: J/↓ &amp; K/↑ navigate the list, ] [ \ set the primary/secondary/both
    /// decision on the selected review item, Del/Backspace discard it. Text-entry controls keep their own keys.
    /// (6.6.)
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ImportViewModel vm)
            return;
        if (e.Source is TextBox or ComboBox)   // don't hijack typing/filtering
            return;

        switch (e.Key)
        {
            case Key.J or Key.Down: vm.MoveSelection(+1); break;
            case Key.K or Key.Up: vm.MoveSelection(-1); break;
            case Key.OemCloseBrackets: vm.DecidePrimary(); break;   // ]
            case Key.OemOpenBrackets: vm.DecideSecondary(); break;  // [
            case Key.OemPipe or Key.OemBackslash: vm.DecideBoth(); break; // \
            case Key.Delete or Key.Back: vm.DiscardSelected(); break;
            default: return;
        }
        e.Handled = true;
    }
}
