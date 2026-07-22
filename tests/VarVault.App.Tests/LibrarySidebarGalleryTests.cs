using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Settings;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Headless UI: library sidebar gallery wall, focus keyboard, unlimited width restore.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class LibrarySidebarGalleryTests
{
    [AvaloniaFact]
    public async Task Sidebar_hosts_shared_gallery_and_survives_wide_width()
    {
        var settings = new FakeSettings();
        settings.Values["library.detail_width"] = "900";
        var detail = new StubDetail([
            new ContentItemDto(1, "Scene", "Saves/scene.json", false, true),
            new ContentItemDto(2, "Look", "Custom/look.vap", true, true),
        ]);
        var vm = new LibraryViewModel(new StubLibrary(1), settings, detail: detail);
        await vm.LoadPreferencesAsync();
        Assert.Equal(900, vm.DetailPanelWidth);

        await vm.RefreshAsync();
        vm.SelectedEntry = vm.Items[0];
        await WaitUntil(() => vm.PackageGallery.Thumbs.Count == 2);

        var view = new LibraryView { DataContext = vm };
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(view.GetVisualDescendants().OfType<PackageGalleryView>(), _ => true);
        Assert.Equal(PackageGalleryMode.Gallery, vm.PackageGallery.Mode);
        var detailCol = view.FindControl<Grid>("RootGrid")!.ColumnDefinitions[3];
        Assert.True(double.IsPositiveInfinity(detailCol.MaxWidth));
        Assert.Equal(PackageGalleryViewModel.MinPanelWidth, detailCol.MinWidth);

        await vm.PackageGallery.EnterFocusAsync(vm.PackageGallery.Thumbs[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.PackageGallery.IsFocusMode);

        await vm.PackageGallery.FocusNextAsync();
        Assert.Equal(2, vm.PackageGallery.SelectedThumb!.ContentItemId);

        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Escape,
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(PackageGalleryMode.Gallery, vm.PackageGallery.Mode);
        Assert.Equal(2, vm.PackageGallery.SelectedThumb!.ContentItemId);
        UiE2E.Screenshot(window, "library-sidebar-gallery-wide");

        // Narrow contact-sheet review.
        window.Width = 900;
        vm.DetailPanelWidth = PackageGalleryViewModel.MinPanelWidth;
        Dispatcher.UIThread.RunJobs();
        UiE2E.Screenshot(window, "library-sidebar-gallery-narrow");

        await vm.PackageGallery.EnterFocusAsync(vm.PackageGallery.Thumbs[0]);
        Dispatcher.UIThread.RunJobs();
        UiE2E.Screenshot(window, "library-sidebar-focus");
    }

    [AvaloniaFact]
    public async Task Var_detail_content_tab_reuses_package_gallery()
    {
        var detail = new StubDetail([
            new ContentItemDto(1, "Scene", "Saves/scene.json", false, true),
        ]);
        var vm = new VarDetailViewModel(detail);
        await vm.LoadCommand.ExecuteAsync(1L);
        vm.SelectedTabIndex = 2;
        await WaitUntil(() => vm.PackageGallery.Thumbs.Count == 1);

        var view = new VarDetailDialog { DataContext = vm };
        var window = new Window { Width = 700, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(view.GetVisualDescendants().OfType<PackageGalleryView>(), _ => true);
    }

    private static async Task WaitUntil(Func<bool> predicate, int timeoutMs = 3000)
    {
        var start = Environment.TickCount64;
        while (!predicate())
        {
            Dispatcher.UIThread.RunJobs();
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("condition not met");
            await Task.Delay(20);
        }
    }

    private sealed class StubLibrary(int count) : ILibraryQueryService
    {
        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken ct = default)
        {
            var items = Enumerable.Range(0, count)
                .Select(i => new PackageListEntry(i + 1, $"C.P{i}.1", "C", $"P{i}", "1", "Scene", 1024, 1, 1, true, false, "Cold", false, null))
                .ToList();
            return Task.FromResult(new LibraryPage(items, count));
        }
        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["C"]);
        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery q, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<long>>(Enumerable.Range(1, count).Select(i => (long)i).ToList());
    }

    private sealed class StubDetail(IReadOnlyList<ContentItemDto> items) : IPackageDetailQuery
    {
        public Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetailOverview?>(new PackageDetailOverview(packageId, "C.P0.1", "k", null, 1, "Hot", 0));

        public Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<ReverseDependentDto>.Empty(request));

        public Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
            long packageId, long? varFileId, PageRequest request, CancellationToken cancellationToken = default,
            string? typeFilter = null, bool loadableOnly = false) =>
            Task.FromResult(new PageResult<ContentItemDto>(items.ToList(), items.Count, request.SafePageNumber, request.SafePageSize));

        public Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<CopyDto>([new CopyDto(10, 1, @"D:\a.var", 1, true, null)], 1, request.SafePageNumber, request.SafePageSize));

        public Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetail?>(new PackageDetail(
                packageId, "C.P0.1", "k", null, 1, "Hot", 0,
                [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]));
    }

    private sealed class FakeSettings : ISettingsService
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
        public async Task<bool> GetBoolAsync(string key, bool fallback = false, CancellationToken cancellationToken = default)
        {
            var raw = await GetAsync(key, cancellationToken).ConfigureAwait(false);
            return bool.TryParse(raw, out var b) ? b : fallback;
        }
        public Task SetBoolAsync(string key, bool value, CancellationToken cancellationToken = default) =>
            SetAsync(key, value ? "true" : "false", cancellationToken);
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(Values);
    }
}
