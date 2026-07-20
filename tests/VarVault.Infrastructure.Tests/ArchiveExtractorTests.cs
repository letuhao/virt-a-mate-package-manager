using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Infrastructure.Import;
using VarVault.Sdk.Import;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Slice B (doc 31 Phase 3.2–3.4): the archive extractor pulls out <c>.var</c> entries, decodes legacy-CJK entry
/// names correctly (the CustomDecoder — the whole reason we didn't trust a lib's codepage guess), and reports
/// truncated/corrupt archives as a status instead of throwing. (7z + password are proven on the real fixture.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class ArchiveExtractorTests
{
    [Fact]
    public async Task Extracts_var_entries_from_a_zip()
    {
        using var dir = new TempDirectory();
        var zip = Path.Combine(dir.Path, "pack.zip");
        using (var fs = new FileStream(zip, FileMode.Create))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(z, "creator.PackA.1.var", "A");
            AddEntry(z, "sub/creator.PackB.2.var", "B");
            AddEntry(z, "readme.txt", "ignored");   // non-var → not extracted
        }

        var dest = Path.Combine(dir.Path, "out");
        var result = await new ArchiveExtractor().ExtractAsync(zip, dest);

        Assert.True(result.Success);
        Assert.Equal(2, result.ExtractedCount);
        Assert.Equal(2, Directory.GetFiles(dest, "*.var", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Decodes_legacy_gbk_entry_name_via_custom_decoder()
    {
        // A zip whose .var entry name is legacy GBK bytes with NO UTF-8 flag → must extract with the right CJK name.
        using var dir = new TempDirectory();
        var zip = Path.Combine(dir.Path, "cjk.zip");
        using (var fs = new FileStream(zip, FileMode.Create))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            AddEntry(z, "ZZZZ.var", "x");           // ASCII placeholder → no UTF-8 flag
        SpliceGbk(zip, "ZZZZ", [0xD2, 0xC2, 0xC8, 0xB9]); // → 衣裙.var (GBK)

        var dest = Path.Combine(dir.Path, "out");
        var result = await new ArchiveExtractor().ExtractAsync(zip, dest);

        Assert.True(result.Success);
        Assert.Equal(1, result.ExtractedCount);
        var extracted = Path.GetFileName(Directory.GetFiles(dest, "*.var", SearchOption.AllDirectories).Single());
        Assert.Contains("衣裙", extracted);          // decoded to the correct Chinese name, not mojibake
        Assert.DoesNotContain("ZZZZ", extracted);
    }

    [Fact]
    public async Task Truncated_archive_reports_corrupt_not_throw()
    {
        using var dir = new TempDirectory();
        var zip = Path.Combine(dir.Path, "pack.zip");
        using (var fs = new FileStream(zip, FileMode.Create))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            AddEntry(z, "creator.Pack.1.var", "content here to make it non-trivial");
        var bytes = File.ReadAllBytes(zip);
        File.WriteAllBytes(zip, bytes[..(bytes.Length / 2)]); // truncate

        var result = await new ArchiveExtractor().ExtractAsync(zip, Path.Combine(dir.Path, "out"));

        Assert.False(result.Success);
        Assert.Equal(ImportSourceStatus.CorruptArchive, result.Status);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    private static void SpliceGbk(string zipPath, string placeholder, byte[] gbk)
    {
        var ascii = Encoding.ASCII.GetBytes(placeholder);
        var bytes = File.ReadAllBytes(zipPath);
        for (var i = 0; i + ascii.Length <= bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < ascii.Length; j++)
                if (bytes[i + j] != ascii[j]) { match = false; break; }
            if (match) { Array.Copy(gbk, 0, bytes, i, gbk.Length); i += ascii.Length - 1; }
        }
        File.WriteAllBytes(zipPath, bytes);
    }
}
