using System.IO;
using VarVault.Domain.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class LooseVarEnumeratorTests
{
    [Fact]
    public void EnumerateVarFiles_skips_link_farm_dirs_and_keeps_loose_root_vars()
    {
        using var root = new TempDirectory();
        File.WriteAllText(Path.Combine(root.Path, "Loose.Pkg.1.var"), "x");
        var varsLink = Path.Combine(root.Path, "___VarsLink___");
        Directory.CreateDirectory(varsLink);
        File.WriteAllText(Path.Combine(varsLink, "Linked.Pkg.1.var"), "y");
        var nested = Path.Combine(root.Path, "Downloads");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "Nested.Pkg.2.var"), "z");

        var found = LooseVarEnumerator.EnumerateVarFiles(root.Path).Select(Path.GetFileName).OrderBy(n => n).ToList();

        Assert.Equal(["Loose.Pkg.1.var", "Nested.Pkg.2.var"], found);
        Assert.Equal(2, LooseVarEnumerator.CountVarFiles(root.Path));
    }

    [Fact]
    public void IsUnderLinkDirectory_detects_vars_link_segment()
    {
        var root = @"F:\VaM\___AddonPacksSwitch ___\Library";
        var bad = Path.Combine(root, "___VarsLink___", "A.B.1.var");
        var good = Path.Combine(root, "A.B.1.var");
        Assert.True(LooseVarEnumerator.IsUnderLinkDirectory(root, bad));
        Assert.False(LooseVarEnumerator.IsUnderLinkDirectory(root, good));
    }
}
