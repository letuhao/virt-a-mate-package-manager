using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Fingerprinting;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// The central-directory reader + signature engine end-to-end: two zips with identical content but
/// different compression/order produce the same ContentSignature (🔒 1.20), and corrupt archives are
/// rejected (1.27). Self-built zips give deterministic proof; a real-repo probe adds D: evidence.
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class ZipCentralDirectoryReaderTests
{
    private static readonly (string Name, byte[] Bytes)[] Content =
    [
        ("meta.json", Encoding.UTF8.GetBytes("{\"licenseType\":\"CC BY\"}")),
        ("Custom/Clothing/衣装/dress.vam", Encoding.UTF8.GetBytes(new string('A', 500))),
        ("Custom/Clothing/衣装/dress.vaj", Encoding.UTF8.GetBytes(new string('B', 1200))),
        ("Saves/scene/看板.json", Encoding.UTF8.GetBytes(new string('C', 3000))),
    ];

    [Fact]
    public void Same_content_different_compression_and_order_yields_same_content_signature()
    {
        using var dir = new TempDirectory();
        var fast = dir.File("fast.var");
        var best = dir.File("best.var");

        WriteZip(fast, Content, CompressionLevel.Fastest, reverse: false);
        WriteZip(best, Content.Reverse().ToArray(), CompressionLevel.SmallestSize, reverse: false);

        // The two files differ byte-for-byte…
        Assert.False(SameBytes(fast, best));

        var sigFast = ContentSignatureEngine.Compute(ReadOk(fast));
        var sigBest = ContentSignatureEngine.Compute(ReadOk(best));

        // …but their logical content signature is identical.
        Assert.Equal(sigFast.ContentSignature, sigBest.ContentSignature);
        Assert.Equal(sigFast.PayloadSignature, sigBest.PayloadSignature);
        Assert.Equal(sigFast.ContentSignatureNoPath, sigBest.ContentSignatureNoPath);
    }

    [Fact]
    public void Different_meta_only_shares_payload_signature_not_content()
    {
        using var dir = new TempDirectory();
        var a = dir.File("a.var");
        var b = dir.File("b.var");

        var contentB = Content.ToArray();
        contentB[0] = ("meta.json", Encoding.UTF8.GetBytes("{\"licenseType\":\"PC\",\"extra\":true}"));

        WriteZip(a, Content, CompressionLevel.Optimal, reverse: false);
        WriteZip(b, contentB, CompressionLevel.Optimal, reverse: false);

        var sa = ContentSignatureEngine.Compute(ReadOk(a));
        var sb = ContentSignatureEngine.Compute(ReadOk(b));

        Assert.Equal(sa.PayloadSignature, sb.PayloadSignature); // payload excludes meta.json
        Assert.NotEqual(sa.ContentSignature, sb.ContentSignature); // full content differs
    }

    [Fact]
    public void Raw_cjk_entry_names_round_trip_without_decoding()
    {
        using var dir = new TempDirectory();
        var path = dir.File("cjk.var");
        WriteZip(path, Content, CompressionLevel.Optimal, reverse: false);

        var entries = ReadOk(path);
        Assert.Contains(entries, e => e.DecodedNameBestEffort.Contains("衣装", StringComparison.Ordinal));
    }

    [Fact]
    public void Corrupt_zip_is_rejected()
    {
        using var dir = new TempDirectory();
        var path = dir.File("broken.var");
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("this is not a zip file at all, no EOCD here"));

        var result = ZipCentralDirectoryReader.Read(path);
        Assert.True(result.IsFailure);
        Assert.Equal("zip.corrupt", result.Error.Code);
    }

    [Fact]
    public void Empty_file_is_rejected()
    {
        using var dir = new TempDirectory();
        var path = dir.File("empty.var");
        File.WriteAllBytes(path, []);

        Assert.True(ZipCentralDirectoryReader.Read(path).IsFailure);
    }

    // D: real-data probe — parses actual vars from the user's corpus and proves signatures are
    // stable across re-reads. Silently no-ops when the corpus isn't present (other machines/CI).
    [Fact]
    public void Real_repo_vars_parse_and_signatures_are_stable()
    {
        const string repo = @"D:\VarVault_test_repo\Vam_Installer_notSameContentFiles1";
        if (!Directory.Exists(repo))
            return;

        var vars = Directory.GetFiles(repo, "*.var").Take(15).ToArray();
        Assert.NotEmpty(vars);

        foreach (var v in vars)
        {
            var first = ZipCentralDirectoryReader.Read(v);
            Assert.True(first.IsSuccess, $"failed to parse {Path.GetFileName(v)}: {first.Error}");
            Assert.NotEmpty(first.Value);

            var s1 = ContentSignatureEngine.Compute(first.Value);
            var s2 = ContentSignatureEngine.Compute(ZipCentralDirectoryReader.Read(v).Value);
            Assert.Equal(s1.ContentSignature, s2.ContentSignature); // deterministic
        }
    }

    private static IReadOnlyList<ZipEntryFacts> ReadOk(string path)
    {
        var result = ZipCentralDirectoryReader.Read(path);
        Assert.True(result.IsSuccess, result.Error.ToString());
        return result.Value;
    }

    private static void WriteZip(string path, (string Name, byte[] Bytes)[] entries, CompressionLevel level, bool reverse)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var entry = zip.CreateEntry(name, level);
            using var s = entry.Open();
            s.Write(bytes);
        }
    }

    private static bool SameBytes(string a, string b)
    {
        var ba = File.ReadAllBytes(a);
        var bb = File.ReadAllBytes(b);
        return ba.AsSpan().SequenceEqual(bb);
    }
}
