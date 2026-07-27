using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Domain.Content;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Unit)]
public sealed class DuplicateEntryFixerTests
{
    [Fact]
    public async Task Explicit_choice_keeps_smaller_case_twin()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "Creator.Dup.1.var");
        var output = Path.Combine(dir.Path, "Creator.Dup.1.dedup.var");
        WriteCaseTwinVar(source, smallPayload: "small", largePayload: new string('x', 2000));

        var key = VamLoadDefectDetector.NormalizeEntryKey("Custom/Atom/Person/Morphs/Mons pubis.vmb");
        var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [key] = "Custom/Atom/Person/Morphs/Mons Pubis.vmb",
            [VamLoadDefectDetector.NormalizeEntryKey("Custom/Atom/Person/Morphs/Mons pubis.vmi")] =
                "Custom/Atom/Person/Morphs/Mons Pubis.vmi",
        };

        var result = await new DuplicateEntryFixer().FixAsync(source, output, choices);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : "");

        using var zip = ZipFile.OpenRead(output);
        Assert.Contains(zip.Entries, e => e.FullName.Contains("Mons Pubis.vmb", StringComparison.Ordinal));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("Mons pubis.vmb", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Keep_larger_removes_case_twin_and_validates_clean()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "Creator.Dup.1.var");
        var output = Path.Combine(dir.Path, "Creator.Dup.1.dedup.var");
        WriteCaseTwinVar(source, smallPayload: "small", largePayload: new string('x', 2000));

        var result = await new DuplicateEntryFixer().FixAsync(source, output);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : "");
        Assert.True(File.Exists(output));
        Assert.Equal(2, result.Value.EntriesDropped);

        var read = ZipCentralDirectoryReader.Read(output);
        Assert.True(read.IsSuccess);
        Assert.DoesNotContain(
            VamLoadDefectDetector.Detect(read.Value),
            d => d.Kind == VamLoadDefectKind.DuplicateEntries);

        // Larger Mons pubis.vmb should survive.
        using var zip = ZipFile.OpenRead(output);
        Assert.Contains(zip.Entries, e => e.FullName.Contains("Mons pubis.vmb", StringComparison.Ordinal));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("Mons Pubis.vmb", StringComparison.Ordinal));
    }

    private static void WriteCaseTwinVar(string path, string smallPayload, string largePayload)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", "{\"creatorName\":\"Creator\",\"packageName\":\"Dup\",\"packageVersion\":\"1\"}");
        Add(zip, "Custom/Atom/Person/Morphs/Mons Pubis.vmb", smallPayload);
        Add(zip, "Custom/Atom/Person/Morphs/Mons pubis.vmb", largePayload);
        Add(zip, "Custom/Atom/Person/Morphs/Mons Pubis.vmi", "a");
        Add(zip, "Custom/Atom/Person/Morphs/Mons pubis.vmi", "bb");
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }
}
