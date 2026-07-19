using VarVault.Domain.Analyzer;
using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class PlacementCapacityTests
{
    [Fact]
    public void Has_room_only_when_free_minus_minfree_covers_the_size()
    {
        // free 1000, minFree 100 → 900 usable.
        Assert.True(PlacementCapacity.HasRoom(freeBytes: 1000, minFreeBytes: 100, sizeBytes: 900));
        Assert.False(PlacementCapacity.HasRoom(freeBytes: 1000, minFreeBytes: 100, sizeBytes: 901));
    }

    [Fact]
    public void Unknown_free_space_means_no_room()
    {
        Assert.False(PlacementCapacity.HasRoom(freeBytes: null, minFreeBytes: 0, sizeBytes: 1));
    }
}

[Trait("Category", TestCategories.Unit)]
public class OrderedSnapshotTests
{
    [Fact]
    public void Indexer_gives_o1_access_in_order()
    {
        var snapshot = new OrderedSnapshot([10L, 20L, 30L]);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(10L, snapshot[0]);
        Assert.Equal(30L, snapshot[2]);
    }

    [Fact]
    public void Position_of_finds_a_row_or_returns_minus_one()
    {
        var snapshot = new OrderedSnapshot([10L, 20L, 30L]);
        Assert.Equal(1, snapshot.PositionOf(20L));
        Assert.Equal(-1, snapshot.PositionOf(99L));
    }
}
