using System.IO;
using System.IO.Compression;
using System.Text;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Extracts the sibling .jpg preview for a content entry; null when there's none. (1.32.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class PreviewExtractorTests
{
    private readonly PreviewExtractor _sut = new();

    [Fact]
    public async Task Extracts_the_sibling_jpg_for_a_scene()
    {
        using var dir = new TempDirectory();
        var path = dir.File("A.B.1.var");
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        WriteVar(path, ("meta.json", Encoding.UTF8.GetBytes("{}")),
                       ("Saves/scene/s.json", Encoding.UTF8.GetBytes("{}")),
                       ("Saves/scene/s.jpg", jpeg));

        var extracted = await _sut.ExtractAsync(path, "Saves/scene/s.json");
        Assert.Equal(jpeg, extracted);
    }

    [Fact]
    public async Task Missing_sibling_jpg_returns_null()
    {
        using var dir = new TempDirectory();
        var path = dir.File("A.B.1.var");
        WriteVar(path, ("meta.json", Encoding.UTF8.GetBytes("{}")),
                       ("Saves/scene/s.json", Encoding.UTF8.GetBytes("{}")));

        Assert.Null(await _sut.ExtractAsync(path, "Saves/scene/s.json"));
    }

    private static void WriteVar(string path, params (string Name, byte[] Bytes)[] entries)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(bytes);
        }
    }
}
