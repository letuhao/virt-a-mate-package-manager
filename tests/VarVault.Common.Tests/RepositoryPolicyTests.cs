using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class RepositoryPathValidatorTests
{
    [Fact]
    public void Rejects_the_addonpackages_folder_and_subfolders()
    {
        var addon = @"C:\VaM\AddonPackages";
        Assert.True(RepositoryPathValidator.Validate(addon, addon, []).IsFailure);
        Assert.True(RepositoryPathValidator.Validate(@"C:\VaM\AddonPackages\sub", addon, []).IsFailure);
    }

    [Fact]
    public void Rejects_overlap_with_an_existing_repository()
    {
        var existing = new[] { @"E:\Repo" };
        Assert.True(RepositoryPathValidator.Validate(@"E:\Repo\child", null, existing).IsFailure);   // nested
        Assert.True(RepositoryPathValidator.Validate(@"E:\Repo", null, existing).IsFailure);          // same
        Assert.True(RepositoryPathValidator.Validate(@"E:\", null, existing).IsFailure);              // parent
    }

    [Fact]
    public void Accepts_a_distinct_path()
    {
        Assert.True(RepositoryPathValidator.Validate(@"F:\Vars", @"C:\VaM\AddonPackages", [@"E:\Repo"]).IsSuccess);
    }
}

[Trait("Category", TestCategories.Unit)]
public class TierPolicyTests
{
    private readonly TierPolicy _policy = TierPolicy.Default;

    [Theory]
    [InlineData(MediaType.Nvme, TierPolicy.HotTier)]
    [InlineData(MediaType.Ssd, TierPolicy.WarmTier)]
    [InlineData(MediaType.Hdd, TierPolicy.ColdTier)]
    [InlineData(MediaType.Unknown, TierPolicy.ColdTier)]
    [InlineData(MediaType.Removable, TierPolicy.ColdTier)]
    [InlineData(MediaType.Network, TierPolicy.ColdTier)]
    public void Assigns_tier_from_media_type_without_a_benchmark(MediaType media, int expectedTier)
    {
        Assert.Equal(expectedTier, _policy.AssignTier(media, readMBps: null));
    }

    [Theory]
    [InlineData(7000, TierPolicy.HotTier)]
    [InlineData(1500, TierPolicy.WarmTier)]
    [InlineData(150, TierPolicy.ColdTier)]
    public void Benchmark_speed_wins_when_available(double readMBps, int expectedTier)
    {
        Assert.Equal(expectedTier, _policy.AssignTier(MediaType.Ssd, readMBps));
    }

    [Fact]
    public void Removable_stays_cold_even_with_a_fast_benchmark()
    {
        Assert.Equal(TierPolicy.ColdTier, _policy.AssignTier(MediaType.Removable, readMBps: 9000));
    }
}
