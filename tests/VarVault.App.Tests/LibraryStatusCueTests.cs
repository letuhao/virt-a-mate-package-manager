using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.Controls;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Library status cues: installed ●, missing-deps pill, favorite mark (mockup / Good-Warn-Crit).</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class LibraryStatusCueTests
{
    [AvaloniaFact]
    public async Task Table_and_gallery_show_installed_missing_and_favorite_cues()
    {
        var entry = new PackageListEntry(
            1, "Creator.Look.1", "Creator", "Look", "1", "Look", 1024,
            1, 1, true, true, "Warm", true, null,
            InstalledAt: DateTime.UtcNow, DependencyCount: 2, IsActive: true);
        var library = new SingleEntryLibrary(entry);
        var vm = new LibraryViewModel(library);
        await vm.RefreshAsync();
        Assert.Single(vm.Items);
        vm.SelectedEntry = vm.Items[0];

        var view = new LibraryView { DataContext = vm };
        var window = new Window { Width = 1100, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Table Package cell: ● + ★ + name
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("●", texts);
        Assert.Contains("★", texts);
        Assert.Contains(view.GetVisualDescendants().OfType<StatePill>(), p => p.Text == "missing");

        // Detail tags
        Assert.Contains(view.GetVisualDescendants().OfType<Tag>(), t => t.Content?.ToString() == "installed");
        Assert.Contains(view.GetVisualDescendants().OfType<Tag>(), t => t.Content?.ToString() == "missing deps");
        Assert.Contains(view.GetVisualDescendants().OfType<Tag>(), t => t.Content?.ToString() == "★ favorite");
    }

    private sealed class SingleEntryLibrary(PackageListEntry entry) : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken ct = default) =>
            Task.FromResult(new LibraryPage([entry], 1));
        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<long>>([entry.PackageId]);
        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([entry.Creator]);
        public Task<IReadOnlyList<CreatorCount>> GetCreatorCountsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreatorCount>>([new CreatorCount(entry.Creator, 1)]);
    }
}
