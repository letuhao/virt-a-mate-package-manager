using SkiaSharp;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// Preview extraction downscales large VaM previews to a gallery thumbnail so the packed cache stays small and
/// grid rendering stays fast at scale — but never loses a preview to a resize failure.
/// </summary>
[Trait("Category", TestCategories.Unit)]
public sealed class PreviewDownscaleTests
{
    private static byte[] Jpeg(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
            canvas.Clear(SKColors.CornflowerBlue);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    private static (int W, int H) Dimensions(byte[] jpeg)
    {
        using var bmp = SKBitmap.Decode(jpeg);
        return (bmp.Width, bmp.Height);
    }

    [Fact]
    public void Large_preview_is_shrunk_within_the_cap_and_smaller()
    {
        var big = Jpeg(1600, 1200);
        var thumb = PreviewExtractor.Downscale(big);

        var (w, h) = Dimensions(thumb);
        Assert.True(System.Math.Max(w, h) <= PreviewExtractor.MaxDimension, $"still {w}x{h}");
        Assert.Equal(1600.0 / 1200.0, (double)w / h, precision: 1);   // aspect ratio preserved
        Assert.True(thumb.Length < big.Length, "thumbnail should be smaller than the raw preview");
    }

    [Fact]
    public void Already_small_preview_is_left_unchanged()
    {
        var small = Jpeg(200, 150);   // within the 384 cap
        Assert.Same(small, PreviewExtractor.Downscale(small));  // returned as-is, no re-encode
    }

    [Fact]
    public void Undecodable_bytes_are_returned_unchanged_not_dropped()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5 };
        Assert.Same(garbage, PreviewExtractor.Downscale(garbage));
    }
}
