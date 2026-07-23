using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Indexing;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class MetaZipRewriterTests
{
    [Fact]
    public async Task Rewrite_replaces_meta_and_keeps_other_entries()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "A.Look.1.var");
        var dest = Path.Combine(dir.Path, "A.Look.1.edited.var");
        WriteVar(source, """{"creatorName":"A","packageName":"Look","dependencies":{"B.Base.1":{}}}""", "Custom/x.vam", "payload");

        var newMeta = MetaJsonEditor.Apply(
            """{"creatorName":"A","packageName":"Look","dependencies":{"B.Base.1":{}}}""",
            "A", "Look", "CC BY", "desc", "1.0",
            ["B.Base.1", "C.Extra.2"]).Value;

        var result = await MetaZipRewriter.RewriteAsync(source, dest, newMeta);
        Assert.True(result.IsSuccess, result.Error.ToString());
        Assert.True(File.Exists(dest));
        Assert.True(File.Exists(source)); // original untouched

        using var zip = ZipFile.OpenRead(dest);
        Assert.NotNull(zip.GetEntry("meta.json"));
        Assert.NotNull(zip.GetEntry("Custom/x.vam"));
        await using var metaStream = zip.GetEntry("meta.json")!.Open();
        using var reader = new StreamReader(metaStream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        Assert.Contains("C.Extra.2", text);
        Assert.Contains("desc", text);
    }

    private static void WriteVar(string path, string meta, string entryName, string entryContent)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var s = zip.CreateEntry("meta.json").Open())
            s.Write(Encoding.UTF8.GetBytes(meta));
        using (var s = zip.CreateEntry(entryName).Open())
            s.Write(Encoding.UTF8.GetBytes(entryContent));
    }
}
