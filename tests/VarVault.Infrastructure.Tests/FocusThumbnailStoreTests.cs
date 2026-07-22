using SkiaSharp;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Focus preview cache is stored separately from 384px wall thumbnails.</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class FocusThumbnailStoreTests
{
    [Fact]
    public async Task Focus_and_wall_content_do_not_overwrite_each_other()
    {
        using var dir = new TempDirectory();
        var store = new ShardedThumbnailStore(dir.Path);
        var wall = Jpeg(64, 48);
        var focus = Jpeg(512, 384);

        await store.PutContentAsync(42, wall);
        await store.PutFocusContentAsync(42, focus);

        var gotWall = await store.GetContentAsync(42);
        var gotFocus = await store.GetFocusContentAsync(42);
        Assert.NotNull(gotWall);
        Assert.NotNull(gotFocus);
        Assert.NotEqual(gotWall!.Length, gotFocus!.Length);
        Assert.True(Dimensions(gotFocus).Longest > Dimensions(gotWall).Longest);
    }

    private static byte[] Jpeg(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(SKColors.MediumPurple);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    private static (int W, int H, int Longest) Dimensions(byte[] jpeg)
    {
        using var bmp = SKBitmap.Decode(jpeg);
        return (bmp.Width, bmp.Height, Math.Max(bmp.Width, bmp.Height));
    }
}
