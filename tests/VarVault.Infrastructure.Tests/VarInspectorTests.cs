using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class VarInspectorTests
{
    private readonly VarInspector _sut = new();

    [Fact]
    public void Inspects_a_healthy_var_end_to_end()
    {
        using var dir = new TempDirectory();
        var path = dir.File("Creator.Package.1.var");
        WriteVar(path, meta: """{"creatorName":"Creator","packageName":"Package","licenseType":"CC BY","dependencies":{"Other.Dep.1":{}}}""",
        [
            ("Saves/scene/s.json", "{}"),
            ("Custom/Clothing/Female/dress.vam", "x"),
        ]);

        var inspection = _sut.Inspect(path).Value;

        Assert.Equal(IntegrityStatus.Ok, inspection.Integrity);
        Assert.NotNull(inspection.Signatures);
        Assert.NotNull(inspection.Classification);
        Assert.Equal(ContentType.Scene, inspection.Classification!.PrimaryType);
        Assert.NotNull(inspection.Meta);
        Assert.Equal("Creator", inspection.Meta!.Creator);
        Assert.Contains("Other.Dep.1", inspection.Meta.DependencyRefs);
        Assert.Equal(Domain.Entities.EncodingHealth.Ok, inspection.Encoding!.Health);
    }

    [Fact]
    public void Missing_meta_is_flagged()
    {
        using var dir = new TempDirectory();
        var path = dir.File("Creator.Package.1.var");
        WriteVarRaw(path, [("Saves/scene/s.json", "{}")]); // no meta.json

        var inspection = _sut.Inspect(path).Value;
        Assert.Equal(IntegrityStatus.MissingMeta, inspection.Integrity);
        Assert.Null(inspection.Meta);
        Assert.NotNull(inspection.Signatures); // still fingerprinted
    }

    [Fact]
    public void Corrupt_payload_crc_is_flagged()
    {
        using var dir = new TempDirectory();
        var path = dir.File("Creator.Package.1.var");
        WriteVar(path, meta: """{"creatorName":"Creator","packageName":"Package","licenseType":"CC BY"}""",
        [
            ("Custom/Clothing/Female/dress.vam", "healthy-payload"),
        ]);

        // Leave payload intact; flip the CD CRC so spot-check detects mismatch (IDX-10).
        PatchFirstCentralDirectoryCrc(path, 0xDEADBEEFu);

        var inspection = _sut.Inspect(path).Value;
        Assert.Equal(IntegrityStatus.CorruptZip, inspection.Integrity);
    }

    /// <summary>Overwrite CRC-32 of the first central-directory file header (offset +16).</summary>
    private static void PatchFirstCentralDirectoryCrc(string path, uint newCrc)
    {
        var bytes = File.ReadAllBytes(path);
        // EOCD at end-22 (no comment): CD offset at +16.
        var eocd = bytes.Length - 22;
        Assert.True(eocd >= 0 && BitConverter.ToUInt32(bytes, eocd) == 0x06054b50u);
        var cdOffset = BitConverter.ToInt32(bytes, eocd + 16);
        Assert.Equal(0x02014b50u, BitConverter.ToUInt32(bytes, cdOffset));
        BitConverter.TryWriteBytes(bytes.AsSpan(cdOffset + 16, 4), newCrc);
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void Corrupt_zip_is_flagged_without_throwing()
    {
        using var dir = new TempDirectory();
        var path = dir.File("broken.var");
        File.WriteAllText(path, "not a zip");

        var inspection = _sut.Inspect(path).Value;
        Assert.Equal(IntegrityStatus.CorruptZip, inspection.Integrity);
        Assert.Empty(inspection.Entries);
    }

    [Fact]
    public void Missing_file_is_a_failure()
    {
        Assert.True(_sut.Inspect(@"Z:\nope\missing.var").IsFailure);
    }

    // D: real-corpus probe — inspect real vars, assert healthy ones parse meta + fingerprint.
    [SkippableFact]
    public void Inspects_real_repo_vars()
    {
        Skip.If(TestCorpus.Primary is null, "requires VARVAULT_TEST_CORPUS");
        var repo = TestCorpus.Primary!;
        var vars = Directory.GetFiles(repo, "*.var", SearchOption.AllDirectories).Take(20).ToArray();
        Assert.NotEmpty(vars);

        var withMeta = 0;
        foreach (var v in vars)
        {
            var result = _sut.Inspect(v);
            Assert.True(result.IsSuccess);
            var inspection = result.Value;
            if (inspection.Integrity == IntegrityStatus.Ok)
            {
                Assert.NotNull(inspection.Signatures);
                if (inspection.Meta is not null)
                    withMeta++;
            }
        }
        Assert.True(withMeta > 0, "expected real vars to carry parseable meta.json");
    }

    private static void WriteVar(string path, string meta, (string Name, string Content)[] entries)
    {
        var all = new List<(string, string)> { ("meta.json", meta) };
        all.AddRange(entries);
        WriteVarRaw(path, all.ToArray());
    }

    private static void WriteVarRaw(string path, (string Name, string Content)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(Encoding.UTF8.GetBytes(content));
        }
    }
}
