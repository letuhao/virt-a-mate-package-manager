using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Converters;
using VarVault.App.ViewModels;
using VarVault.Sdk.Import;
using VarVault.Sdk.Indexing;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// Slice D · doc 31 Phase 6.3: incoming vars aren't catalogued, so the gallery gets its preview from a per-file
/// extraction done during scan. This proves a preview-bearing var yields a real on-disk thumb that the gallery's
/// path→<see cref="Bitmap"/> converter decodes (≥1 real preview), while an image-less var falls back to placeholder.
/// </summary>
public sealed class ImportGalleryPreviewTests
{
    [AvaloniaFact]
    public async Task Gallery_extracts_and_decodes_a_real_var_preview()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var targetDir = new TempDirectory();
        using var importDir = new TempDirectory();

        var png = RenderPng();   // a real PNG produced by the Skia backend → guaranteed decodable
        WriteVarWithPreview(Path.Combine(importDir.Path, "Look.Girl.1.var"), "Look", "Girl", png);
        WriteVarNoImage(Path.Combine(importDir.Path, "Plain.Morph.1.var"), "Plain", "Morph");

        Guid targetId;
        using (var scope = host.Host.Services.CreateScope())
        {
            var repos = scope.ServiceProvider.GetRequiredService<IRepositoryService>();
            targetId = (await repos.RegisterAsync(new RegisterRepositoryRequest("target", targetDir.Path))).Value.Id;
        }
        using (var scope = host.Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IIndexOrchestrator>().IndexAllAsync();

        using var read = host.Host.Services.CreateScope();
        var vm = new ImportViewModel(
            read.ServiceProvider.GetRequiredService<IImportService>(),
            read.ServiceProvider.GetRequiredService<IRepositoryService>());
        await vm.LoadAsync();
        vm.TargetRepo = vm.Repositories.First(r => r.Id == targetId);
        vm.AddSourcePath(importDir.Path);
        await vm.ScanCommand.ExecuteAsync(null);

        var withPreview = vm.Items.First(i => i.FileName == "Look.Girl.1.var");
        var noPreview = vm.Items.First(i => i.FileName == "Plain.Morph.1.var");

        Assert.True(withPreview.HasPreview);
        Assert.False(string.IsNullOrEmpty(withPreview.PreviewPath));
        Assert.True(File.Exists(withPreview.PreviewPath!), "extracted preview should be on disk in the session temp");
        Assert.False(noPreview.HasPreview);   // no image entry → placeholder

        // The gallery binding decodes the extracted file into a Bitmap.
        var bmp = PathToBitmapConverter.Instance.Convert(withPreview.PreviewPath, typeof(Bitmap), null, CultureInfo.InvariantCulture);
        Assert.IsType<Bitmap>(bmp);
        Assert.Null(PathToBitmapConverter.Instance.Convert(noPreview.PreviewPath, typeof(Bitmap), null, CultureInfo.InvariantCulture));
    }

    private static byte[] RenderPng()
    {
        var rtb = new RenderTargetBitmap(new PixelSize(8, 8), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
            ctx.DrawRectangle(Brushes.CornflowerBlue, null, new Rect(0, 0, 8, 8));
        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }

    private static string Meta(string creator, string package) =>
        "{\"creatorName\":\"" + creator + "\",\"packageName\":\"" + package + "\",\"dependencies\":{}}";

    private static void WriteVarWithPreview(string path, string creator, string package, byte[] png)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", Encoding.UTF8.GetBytes(Meta(creator, package)));
        Add(zip, "Saves/scene/Look.json", Encoding.UTF8.GetBytes("{\"scene\":true}"));
        Add(zip, "Saves/scene/Look.png", png);
    }

    private static void WriteVarNoImage(string path, string creator, string package)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "meta.json", Encoding.UTF8.GetBytes(Meta(creator, package)));
        Add(zip, "Custom/Atom/Morph/m.vmi", Encoding.UTF8.GetBytes("morph"));
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        s.Write(content);
    }
}
