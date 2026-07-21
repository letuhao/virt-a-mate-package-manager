using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class RepositorySignatureTests
{
    [Fact]
    public void Empty_is_zero()
    {
        var sig = RepositorySignature.Empty;
        Assert.Equal(0, sig.FileCount);
        Assert.Equal(0, sig.TotalBytes);
        Assert.Equal(0, sig.NewestMtimeTicks);
        Assert.Equal(0, sig.PathsHash);
    }

    [Fact]
    public void Add_accumulates_count_bytes_and_newest_mtime()
    {
        var t1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var sig = RepositorySignature.Empty
            .Add(100, t1, "A.One.1.var")
            .Add(200, t2, "B.Two.1.var");

        Assert.Equal(2, sig.FileCount);
        Assert.Equal(300, sig.TotalBytes);
        Assert.Equal(t2.Ticks, sig.NewestMtimeTicks);
        Assert.NotEqual(0, sig.PathsHash);
    }

    [Fact]
    public void Add_is_order_independent()
    {
        var t1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        var forward = RepositorySignature.Empty
            .Add(100, t1, "A.One.1.var")
            .Add(200, t2, "B.Two.1.var");

        var reverse = RepositorySignature.Empty
            .Add(200, t2, "B.Two.1.var")
            .Add(100, t1, "A.One.1.var");

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Different_paths_produce_different_signatures()
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = RepositorySignature.Empty.Add(100, t, "A.One.1.var");
        var b = RepositorySignature.Empty.Add(100, t, "B.Two.1.var");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_sizes_produce_different_signatures()
    {
        var t = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var a = RepositorySignature.Empty.Add(100, t, "A.One.1.var");
        var b = RepositorySignature.Empty.Add(101, t, "A.One.1.var");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_mtimes_produce_different_signatures()
    {
        var t1 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var a = RepositorySignature.Empty.Add(100, t1, "A.One.1.var");
        var b = RepositorySignature.Empty.Add(100, t2, "A.One.1.var");
        Assert.NotEqual(a, b);
    }
}
