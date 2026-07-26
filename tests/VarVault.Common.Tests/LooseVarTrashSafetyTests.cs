using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class LooseVarTrashSafetyTests
{
    [Fact]
    public void Profile_path_under_AddonPacksSwitch_is_not_a_symlink_farm()
    {
        var path = @"F:\VaM\___AddonPacksSwitch ___\Library installs\Fresh.Look.1.var";
        Assert.False(LooseVarEnumerator.PathContainsSymlinkFarm(path));
        Assert.False(LooseVarEnumerator.IsSymlinkFarmDirectory("___AddonPacksSwitch ___"));
        Assert.False(LooseVarEnumerator.IsSymlinkFarmDirectory("Library installs"));
    }

    [Fact]
    public void VarsLink_segment_is_a_symlink_farm()
    {
        var path = @"F:\VaM\___AddonPacksSwitch ___\Library installs\___VarsLink___\Fresh.Look.1.var";
        Assert.True(LooseVarEnumerator.PathContainsSymlinkFarm(path));
        Assert.True(LooseVarEnumerator.IsSymlinkFarmDirectory("___VarsLink___"));
    }
}
