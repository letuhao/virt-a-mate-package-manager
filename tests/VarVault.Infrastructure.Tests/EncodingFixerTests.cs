using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// The flagship encoding auto-fix: a var with GBK entry names (no UTF-8 flag) — which VaM can't load —
/// is rewritten to a valid UTF-8 var, validated against VaM's constraints, with the original retained.
/// (IDX-9; checklist 4.12/4.13/4.14.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class EncodingFixerTests
{
    private const int Gbk = 936;
    private readonly EncodingFixer _sut = new();

    static EncodingFixerTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public async Task Fixes_a_gbk_broken_var_into_valid_utf8()
    {
        using var dir = new TempDirectory();
        var broken = dir.File("Creator.Broken.1.var");
        var fixedOut = dir.File("Creator.Broken.1.fixed.var");
        WriteBrokenGbkVar(broken);

        // Precondition: detection flags it as needing a fix.
        var before = EncodingHealthEngine.Detect(ZipCentralDirectoryReader.Read(broken).Value);
        Assert.Equal(EncodingHealth.NeedsFix, before.Health);
        Assert.Equal("GBK", before.DetectedCodepage);

        var result = await _sut.FixAsync(broken, Gbk, fixedOut);
        Assert.True(result.IsSuccess, result.Error.ToString());

        // The fixed var is healthy UTF-8 and VaM-valid.
        var fixedEntries = ZipCentralDirectoryReader.Read(fixedOut).Value;
        Assert.Equal(EncodingHealth.Ok, EncodingHealthEngine.Detect(fixedEntries).Health);
        Assert.True(VamVarValidator.Validate(fixedEntries).IsSuccess);
        Assert.Contains(fixedEntries, e => !e.IsDirectory && !IsAscii(e.RawNameBytes) && e.NameIsUtf8);
        Assert.Contains(fixedEntries, e => e.DecodedNameBestEffort.Contains("衣装", StringComparison.Ordinal));

        // The original is retained, untouched. (4.14)
        Assert.True(File.Exists(broken));
        Assert.Equal(EncodingHealth.NeedsFix, EncodingHealthEngine.Detect(ZipCentralDirectoryReader.Read(broken).Value).Health);
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_existing_output()
    {
        using var dir = new TempDirectory();
        var broken = dir.File("a.var");
        WriteBrokenGbkVar(broken);
        var output = dir.File("out.var");
        File.WriteAllText(output, "existing");

        var result = await _sut.FixAsync(broken, Gbk, output);
        Assert.True(result.IsFailure); // never overwrite in place (4.12)
    }

    [Fact]
    public async Task No_partial_file_remains_after_success()
    {
        using var dir = new TempDirectory();
        var broken = dir.File("a.var");
        WriteBrokenGbkVar(broken);
        var output = dir.File("out.var");

        await _sut.FixAsync(broken, Gbk, output);
        Assert.False(File.Exists(output + ".partial"));
    }

    private static void WriteBrokenGbkVar(string path)
    {
        // Names written in GBK without the UTF-8 flag = the exact "VaM can't load it" case.
        var gbk = Encoding.GetEncoding(Gbk);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, gbk);
        Add(zip, "meta.json", "{\"creatorName\":\"Creator\",\"packageName\":\"Broken\"}");
        Add(zip, "Custom/Clothing/衣装/裙子.vam", "x");
        Add(zip, "Custom/Clothing/衣装/裙子.vaj", "y");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    private static bool IsAscii(byte[] raw) => raw.All(b => b < 0x80);
}
