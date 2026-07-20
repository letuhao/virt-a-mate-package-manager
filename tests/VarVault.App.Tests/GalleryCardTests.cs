using VarVault.App.ViewModels;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// doc 26 · G-3 — gallery cards were bare (audit 25 §5.2: no tier/state/favorite/installed pills or size,
/// though all fields exist on the DTO). This proves the card now surfaces them.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class GalleryCardTests
{
    [Fact]
    public void Card_exposes_tier_favorite_size_and_installed()
    {
        var entry = new PackageListEntry(
            1, "A.B.1", "A", "B", "1", "Look", 2L << 30, OnlineInstanceCount: 1, TotalInstanceCount: 1,
            IsSingleCopy: true, IsFavorite: true, StorageClass: "Warm", HasMissingDeps: false,
            LastUsedAt: null, Tier: 2, ContentCounts: null, AddedAt: null, DependencyCount: 0, IsActive: true);

        var card = new GalleryCardViewModel(entry, loader: null);

        Assert.Equal("T2", card.TierLabel);
        Assert.True(card.IsFavorite);
        Assert.True(card.IsActive);
        Assert.True(card.IsSingleCopy);
        Assert.Equal("Warm", card.StorageClass);
        Assert.Equal("2.0 GB", card.SizeText);
    }

    [Fact]
    public void Card_size_uses_megabytes_below_a_gigabyte()
    {
        var entry = new PackageListEntry(
            1, "A.B.1", "A", "B", "1", "Scene", 512L << 20, 1, 1,
            false, false, "Cold", false, null, Tier: 3);

        var card = new GalleryCardViewModel(entry, loader: null);

        Assert.Equal("512 MB", card.SizeText);
        Assert.Equal("T3", card.TierLabel);
    }
}
