using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GD-2/GD-3 · Library rail + facet wiring: saved-view filters, reset, sort dropdown, maintenance navigation.
/// (18-gap GD-2/GD-3.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class LibraryGapTests
{
    [Fact]
    public async Task Reset_clears_all_filters()
    {
        var vm = new LibraryViewModel(new StubLibraryQuery())
        {
            CreatorFilter = "MeshedVR",
            PackageNameFilter = "abc",
            FavoritesOnly = true,
        };
        await vm.ResetFiltersCommand.ExecuteAsync(null);
        Assert.Null(vm.CreatorFilter);
        Assert.Null(vm.PackageNameFilter);
        Assert.False(vm.FavoritesOnly);
    }

    [Fact]
    public async Task Show_missing_deps_sets_the_filter()
    {
        var vm = new LibraryViewModel(new StubLibraryQuery());
        await vm.ShowMissingDepsCommand.ExecuteAsync(null);
        Assert.True(vm.MissingDepsOnly);
        Assert.False(vm.FavoritesOnly);
    }

    [Fact]
    public void Maintenance_tool_navigates_the_shell()
    {
        var vm = new LibraryViewModel(new StubLibraryQuery());
        string? navigated = null;
        vm.NavigateTo = id => navigated = id;
        vm.GoCommand.Execute("dupes");
        Assert.Equal("dupes", navigated);
    }

    [Fact]
    public void Sort_dropdown_label_drives_the_sort()
    {
        var vm = new LibraryViewModel(new StubLibraryQuery());
        Assert.Contains("Size", vm.SortOptions);
        vm.SelectedSortLabel = "Size";
        Assert.Equal(LibrarySort.Size, vm.Sort);
    }
}
