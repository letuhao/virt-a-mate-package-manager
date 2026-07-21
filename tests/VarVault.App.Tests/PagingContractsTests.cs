using VarVault.Sdk.Paging;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>Paging contract math: clamp, skip, totals, and empty-page helpers.</summary>
[Trait("Category", TestCategories.Unit)]
public sealed class PagingContractsTests
{
    [Theory]
    [InlineData(0, 50, 1, 50, 0)]
    [InlineData(-3, 0, 1, 1, 0)]
    [InlineData(2, 1000, 2, 100, 100)]
    [InlineData(3, 25, 3, 25, 50)]
    public void PageRequest_normalizes_page_and_size(int page, int size, int expectPage, int expectSize, int expectSkip)
    {
        var request = new PageRequest(page, size).Normalize();
        Assert.Equal(expectPage, request.PageNumber);
        Assert.Equal(expectSize, request.PageSize);
        Assert.Equal(expectSkip, request.Skip);
    }

    [Fact]
    public void PageResult_reports_window_and_page_count()
    {
        var page = new PageResult<int>([1, 2, 3], TotalCount: 120, PageNumber: 2, PageSize: 50);
        Assert.Equal(3, page.PageCount);
        Assert.Equal(51, page.FirstItemNumber);
        Assert.Equal(53, page.LastItemNumber);

        var empty = PageResult<int>.Empty(new PageRequest(4, 50));
        Assert.Equal(0, empty.TotalCount);
        Assert.Equal(1, empty.PageCount);
        Assert.Equal(0, empty.FirstItemNumber);
        Assert.Equal(0, empty.LastItemNumber);
    }
}
