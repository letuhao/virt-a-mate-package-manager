using VarVault.Sdk.Indexer;
using VarVault.TestKit;

namespace VarVault.Host.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class IndexerProtocolTests
{
    [Fact]
    public void HashDataDirectory_trims_trailing_separators()
    {
        var a = IndexerProtocol.HashDataDirectory(@"D:\Program Files\VarVault");
        var b = IndexerProtocol.HashDataDirectory(@"D:\Program Files\VarVault\");
        var c = IndexerProtocol.HashDataDirectory(@"D:\Program Files\VarVault/");
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal(16, a.Length); // 8 bytes as hex
    }
}
