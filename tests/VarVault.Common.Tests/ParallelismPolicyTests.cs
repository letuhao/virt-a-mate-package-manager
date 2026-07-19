using VarVault.Domain.Entities;
using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class ParallelismPolicyTests
{
    [Theory]
    [InlineData(MediaType.Nvme, 8)]
    [InlineData(MediaType.Ssd, 4)]
    [InlineData(MediaType.Network, 2)]
    [InlineData(MediaType.Hdd, 1)]
    [InlineData(MediaType.Removable, 1)]
    [InlineData(MediaType.Unknown, 1)]
    public void Degree_matches_media_type(MediaType media, int expected)
    {
        Assert.Equal(expected, ParallelismPolicy.DegreeFor(media));
    }

    [Fact]
    public void Hdd_never_runs_concurrent_reads()
    {
        Assert.Equal(1, ParallelismPolicy.DegreeFor(MediaType.Hdd));
    }
}
