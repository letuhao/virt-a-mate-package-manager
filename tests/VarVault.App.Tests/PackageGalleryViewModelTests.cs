using Avalonia.Media.Imaging;
using SkiaSharp;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.Domain.Indexing;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Library sidebar / shared package gallery: modes, navigation, filters, stale cancel.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class PackageGalleryViewModelTests
{
    [Fact]
    public async Task Defaults_to_gallery_mode_and_enters_focus_on_thumb_click()
    {
        var detail = new StubDetail([
            Item(1, "Scene", "a.json"),
            Item(2, "Look", "b.vap"),
        ]);
        using var gallery = new PackageGalleryViewModel(detail);
        await gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);

        Assert.Equal(PackageGalleryMode.Gallery, gallery.Mode);
        Assert.Equal(2, gallery.Thumbs.Count);

        await gallery.EnterFocusAsync(gallery.Thumbs[0]);
        Assert.Equal(PackageGalleryMode.Focus, gallery.Mode);
        Assert.Equal(1, gallery.SelectedThumb!.ContentItemId);

        gallery.ExitFocus();
        Assert.Equal(PackageGalleryMode.Gallery, gallery.Mode);
        Assert.Equal(1, gallery.SelectedThumb!.ContentItemId); // selection kept
    }

    [Fact]
    public async Task Focus_navigation_stays_within_preview_eligible_bounds()
    {
        var detail = new StubDetail([
            Item(1, "Scene", "a.json"),
            Item(2, "Asset", "mesh.vam", hasPreview: false),
            Item(3, "Look", "b.vap"),
        ]);
        using var gallery = new PackageGalleryViewModel(detail);
        await gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);

        await gallery.EnterFocusAsync(gallery.Thumbs[0]);
        Assert.Equal("1 / 2", gallery.FocusCounter);

        await gallery.FocusNextAsync();
        Assert.Equal(3, gallery.SelectedThumb!.ContentItemId);
        Assert.False(gallery.CanGoNext);

        await gallery.FocusPreviousAsync();
        Assert.Equal(1, gallery.SelectedThumb!.ContentItemId);
        Assert.False(gallery.CanGoPrevious);

        await gallery.FocusLastAsync();
        Assert.Equal(3, gallery.SelectedThumb!.ContentItemId);
        await gallery.FocusFirstAsync();
        Assert.Equal(1, gallery.SelectedThumb!.ContentItemId);
    }

    [Fact]
    public async Task Type_filter_and_loadable_reload_content_and_exit_focus()
    {
        var detail = new StubDetail([
            Item(1, "Scene", "a.json"),
            Item(2, "Look", "b.vap", isPreset: true),
        ]);
        using var gallery = new PackageGalleryViewModel(detail);
        await gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);
        await gallery.EnterFocusAsync(gallery.Thumbs[0]);

        gallery.TypeFilter = "Look";
        await WaitUntil(() => gallery.Mode == PackageGalleryMode.Gallery && !gallery.ContentPager.IsLoading);
        Assert.Equal(PackageGalleryMode.Gallery, gallery.Mode);
        Assert.Equal("Look", detail.LastTypeFilter);

        gallery.LoadableOnly = true;
        await WaitUntil(() => !gallery.ContentPager.IsLoading);
        Assert.True(detail.LastLoadableOnly);
    }

    [Fact]
    public async Task Changing_package_clears_immediately_and_cancels_stale_bind()
    {
        var gate = new TaskCompletionSource();
        var detail = new DelayingDetail(gate.Task, [
            Item(1, "Scene", "a.json"),
        ]);
        using var gallery = new PackageGalleryViewModel(detail);

        var first = gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);
        Assert.Equal(0, gallery.Thumbs.Count); // cleared before await completes
        Assert.Equal(1, gallery.PackageId);

        gallery.Clear();
        gate.SetResult();
        await first;

        Assert.Equal(0, gallery.PackageId);
        Assert.Empty(gallery.Thumbs);
        Assert.Equal(PackageGalleryMode.Gallery, gallery.Mode);
    }

    [Fact]
    public async Task Empty_preview_list_does_not_crash_focus_navigation()
    {
        var detail = new StubDetail([]);
        using var gallery = new PackageGalleryViewModel(detail);
        await gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);

        await gallery.FocusNextAsync();
        await gallery.FocusPreviousAsync();
        await gallery.FocusFirstAsync();
        await gallery.FocusLastAsync();
        Assert.Equal(PackageGalleryMode.Gallery, gallery.Mode);
    }

    [Fact]
    public async Task Same_package_row_refresh_does_not_clear_gallery()
    {
        var detail = new StubDetail([Item(1, "Scene", "a.json")]);
        var library = new StickyLibrary();
        var vm = new LibraryViewModel(library, detail: detail);
        await vm.RefreshAsync();
        vm.SelectedEntry = vm.Items[0];
        await WaitUntil(() => vm.PackageGallery.Thumbs.Count == 1);
        var thumbsBefore = vm.PackageGallery.Thumbs.Count;

        // Favorite toggles replace SelectedEntry with a new record of the same PackageId.
        var updated = vm.SelectedEntry! with { IsFavorite = true };
        vm.SelectedEntry = updated;

        Assert.Equal(thumbsBefore, vm.PackageGallery.Thumbs.Count);
        Assert.Equal(1, vm.PackageGallery.PackageId);
        Assert.False(vm.PackageGallery.IsBinding);
    }

    [Fact]
    public async Task Focus_uses_high_res_bytes_when_store_has_focus_cache()
    {
        var wall = Jpeg(64, 48);
        var focus = Jpeg(800, 600);
        var store = new CapturingThumbStore();
        store.Wall[7] = wall;
        store.Focus[7] = focus;
        var detail = new StubDetail([Item(7, "Scene", "s.json")]);
        using var gallery = new PackageGalleryViewModel(detail, store);
        await gallery.BindPackageAsync(1, [new CopyDto(10, 1, @"D:\a.var", 1, true, null)]);

        await gallery.EnterFocusAsync(gallery.Thumbs[0]);
        await WaitUntil(() => gallery.FocusBitmap is not null || gallery.FocusStatus is not null);

        Assert.NotNull(gallery.FocusBitmap);
        Assert.False(gallery.FocusIsFallback);
        Assert.Equal(1, store.FocusGets);
        Assert.Equal(0, store.WallGets); // did not fall back to wall
    }

    [Fact]
    public void Focus_bitmap_cache_evicts_and_disposes_lru()
    {
        using var cache = new FocusBitmapCache(2);
        var a = TinyBitmap();
        var b = TinyBitmap();
        var c = TinyBitmap();
        cache.Set(1, a);
        cache.Set(2, b);
        Assert.True(cache.TryGet(1, out _)); // touch 1 → 2 becomes LRU
        cache.Set(3, c);
        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(3, out _));
    }

    [Fact]
    public void Focus_bitmap_cache_does_not_dispose_pinned_image()
    {
        using var cache = new FocusBitmapCache(1);
        var pinned = TinyBitmap();
        var other = TinyBitmap();
        cache.Set(1, pinned);
        cache.PinnedId = 1;
        cache.Set(2, other); // capacity 1 → other is trimmed; pin survives
        Assert.True(cache.TryGet(1, out var still));
        Assert.Same(pinned, still);
        Assert.False(cache.TryGet(2, out _));

        cache.PinnedId = 3;
        var newest = TinyBitmap();
        cache.Set(3, newest); // pin newest before/as insert so it isn't the LRU victim
        Assert.True(cache.TryGet(3, out var kept));
        Assert.Same(newest, kept);
        Assert.False(cache.TryGet(1, out _));
    }

    private static ContentItemDto Item(long id, string type, string path, bool hasPreview = true, bool isPreset = false) =>
        new(id, type, path, isPreset, hasPreview);

    private static async Task WaitUntil(Func<bool> predicate, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (!predicate())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("condition not met");
            await Task.Delay(20);
        }
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(SKColors.Orange);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static Bitmap TinyBitmap()
    {
        using var bmp = new SKBitmap(4, 4);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(new MemoryStream(data.ToArray()));
    }

    private sealed class StickyLibrary : ILibraryQueryService
    {
        private readonly PackageListEntry _entry = new(
            1, "A.B.1", "A", "B", "1", "Scene", 100, 1, 1, true, false, "Hot", false, null);

        public Task<LibraryPage> GetPageAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryPage([_entry], 1));

        public Task<IReadOnlyList<string>> GetCreatorsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["A"]);

        public Task<IReadOnlyList<long>> GetOrderedIdsAsync(LibraryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<long>>([1]);
    }

    private sealed class StubDetail(IReadOnlyList<ContentItemDto> items) : IPackageDetailQuery
    {
        public string? LastTypeFilter { get; private set; }
        public bool LastLoadableOnly { get; private set; }

        public Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetailOverview?>(null);

        public Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<ReverseDependentDto>.Empty(request));

        public Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
            long packageId, long? varFileId, PageRequest request, CancellationToken cancellationToken = default,
            string? typeFilter = null, bool loadableOnly = false)
        {
            LastTypeFilter = typeFilter;
            LastLoadableOnly = loadableOnly;
            IEnumerable<ContentItemDto> q = items;
            if (loadableOnly)
                q = q.Where(i => i.IsPreset || i.Type == "Scene");
            if (!string.IsNullOrWhiteSpace(typeFilter))
                q = q.Where(i => string.Equals(i.Type, typeFilter, StringComparison.OrdinalIgnoreCase));
            var list = q.ToList();
            return Task.FromResult(new PageResult<ContentItemDto>(list, list.Count, request.SafePageNumber, request.SafePageSize));
        }

        public Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<CopyDto>.Empty(request));

        public Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetail?>(new PackageDetail(packageId, "A.B.1", "k", null, 1, "Hot", 0, []));
    }

    private sealed class DelayingDetail(Task gate, IReadOnlyList<ContentItemDto> items) : IPackageDetailQuery
    {
        public Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetailOverview?>(null);

        public Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<ReverseDependentDto>.Empty(request));

        public Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<DependencyEdgeDto>.Empty(request));

        public async Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
            long packageId, long? varFileId, PageRequest request, CancellationToken cancellationToken = default,
            string? typeFilter = null, bool loadableOnly = false)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new PageResult<ContentItemDto>(items.ToList(), items.Count, request.SafePageNumber, request.SafePageSize);
        }

        public Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PageResult<CopyDto>.Empty(request));

        public Task<PackageDetail?> GetAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetail?>(new PackageDetail(packageId, "A.B.1", "k", null, 1, "Hot", 0, []));
    }

    private sealed class CapturingThumbStore : IThumbnailStore
    {
        public Dictionary<long, byte[]> Wall { get; } = new();
        public Dictionary<long, byte[]> Focus { get; } = new();
        public int FocusGets { get; private set; }
        public int WallGets { get; private set; }

        public Task PutAsync(long packageId, byte[] jpeg, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<byte[]?> GetAsync(long packageId, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);
        public Task<bool> ExistsAsync(long packageId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task PutContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default)
        {
            Wall[contentItemId] = jpeg;
            return Task.CompletedTask;
        }
        public Task<byte[]?> GetContentAsync(long contentItemId, CancellationToken cancellationToken = default)
        {
            WallGets++;
            return Task.FromResult(Wall.TryGetValue(contentItemId, out var b) ? b : null);
        }
        public Task PutFocusContentAsync(long contentItemId, byte[] jpeg, CancellationToken cancellationToken = default)
        {
            Focus[contentItemId] = jpeg;
            return Task.CompletedTask;
        }
        public Task<byte[]?> GetFocusContentAsync(long contentItemId, CancellationToken cancellationToken = default)
        {
            FocusGets++;
            return Task.FromResult(Focus.TryGetValue(contentItemId, out var b) ? b : null);
        }
    }
}
