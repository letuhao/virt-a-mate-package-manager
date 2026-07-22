using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Sdk.Library;
using VarVault.Sdk.Paging;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-10 · Var detail loads + renders detail fields. (16-checklist DLG-10.)</summary>
[Trait("Category", TestCategories.Unit)]
public class VarDetailDialogTests
{
    private sealed class StubDetail : IPackageDetailQuery
    {
        public Task<PackageDetailOverview?> GetOverviewAsync(long packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PackageDetailOverview?>(new PackageDetailOverview(1, "五一莉刻.精神分裂1号.1", "KEY", "CC BY", 514, "Hot", 27));

        public Task<PageResult<DependencyEdgeDto>> GetDirectDependenciesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<DependencyEdgeDto>([], 0, request.SafePageNumber, request.SafePageSize));

        public Task<PageResult<ReverseDependentDto>> GetReverseDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<ReverseDependentDto>([], 0, request.SafePageNumber, request.SafePageSize));

        public Task<PageResult<DependencyEdgeDto>> GetSaveDependentsPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<DependencyEdgeDto>([], 0, request.SafePageNumber, request.SafePageSize));

        public Task<PageResult<ContentItemDto>> GetContentItemsPageAsync(
            long packageId,
            long? varFileId,
            PageRequest request,
            CancellationToken cancellationToken = default,
            string? typeFilter = null,
            bool loadableOnly = false) =>
            Task.FromResult(new PageResult<ContentItemDto>([new ContentItemDto(1, "Scene", "s.json", false, true)], 1, request.SafePageNumber, request.SafePageSize));

        public Task<PageResult<CopyDto>> GetCopiesPageAsync(long packageId, PageRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PageResult<CopyDto>([new CopyDto(1, 1, "p", 100, true, null)], 1, request.SafePageNumber, request.SafePageSize));

        public Task<PackageDetail?> GetAsync(long id, CancellationToken ct = default) =>
            Task.FromResult<PackageDetail?>(new PackageDetail(id, "五一莉刻.精神分裂1号.1", "KEY", "CC BY", 514,
                "Hot", 27, [new CopyDto(1, 1, "p", 100, true, null)]));
    }

    [AvaloniaFact]
    public async Task Loads_and_renders_detail()
    {
        var vm = new VarDetailViewModel(new StubDetail());
        await vm.LoadCommand.ExecuteAsync(1L);
        Assert.NotNull(vm.Detail);
        Assert.Equal(27, vm.Detail!.DependedOnByCount);

        var view = new VarDetailDialog { DataContext = vm };
        var window = new Window { Width = 600, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("五一莉刻.精神分裂1号.1", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
