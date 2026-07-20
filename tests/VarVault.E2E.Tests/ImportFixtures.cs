using System.IO;
using System.IO.Compression;
using System.Text;

namespace VarVault.E2E.Tests;

/// <summary>Shared var/archive builders for the import scan + apply E2E tests.</summary>
internal static class ImportFixtures
{
    public static string Meta(string creator, string package) =>
        "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{}}";

    public static void WriteVar(string dir, string fileName, string creator, string package, (string, string)[] entries)
    {
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", Meta(creator, package));
        foreach (var (name, content) in entries)
            Add(zip, name, content);
    }

    public static void Add(ZipArchive zip, string name, string content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    public static void WriteBadZip(string path) => File.WriteAllText(path, "this is not a zip archive at all\n");

    /// <summary>A 1×1 PNG — a real, decodable image so gallery-preview extraction + decode can be proven.</summary>
    public static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    /// <summary>A valid var carrying a scene JSON + its sibling preview image (so gallery preview extraction hits). </summary>
    public static void WriteVarWithPreview(string dir, string fileName, string creator, string package)
    {
        using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", Meta(creator, package));
        Add(zip, "Saves/scene/Look.json", "{\"scene\":true}");
        using var s = zip.CreateEntry("Saves/scene/Look.png", CompressionLevel.Optimal).Open();
        s.Write(OnePixelPng);
    }

    /// <summary>A valid var whose one entry name is legacy GBK bytes with no UTF-8 flag (VaM-unloadable).</summary>
    public static void WriteGbkVar(string path, string creator, string package)
    {
        byte[] gbk = [0xD2, 0xC2, 0xC8, 0xB9, 0xD0, 0xAC]; // 衣裙鞋
        byte[] placeholder = Encoding.ASCII.GetBytes("ZZZZZZ");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            Add(zip, "meta.json", Meta(creator, package));
            Add(zip, "Custom/ZZZZZZ.vam", "cjk placeholder");
        }
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + placeholder.Length <= bytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < placeholder.Length; j++)
                if (bytes[i + j] != placeholder[j]) { match = false; break; }
            if (match) { Array.Copy(gbk, 0, bytes, i, gbk.Length); i += placeholder.Length - 1; }
        }
        File.WriteAllBytes(path, bytes);
    }
}
