using System.IO;
using System.IO.Compression;
using SkiaSharp;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Extracts the sibling <c>.jpg</c> preview for a content entry inside a var and downscales it.
/// Wall thumbnails use <see cref="MaxDimension"/> (384); focus viewer uses <see cref="FocusMaxDimension"/>.
/// (Checklist 1.32; Library sidebar gallery.)
/// </summary>
public sealed class PreviewExtractor
{
    /// <summary>Longest-edge cap for a stored wall thumbnail.</summary>
    public const int MaxDimension = 384;

    /// <summary>Longest-edge cap for the on-demand focus viewer image (sidebar can be very wide).</summary>
    public const int FocusMaxDimension = 1536;

    private const int JpegQuality = 80;
    private const int FocusJpegQuality = 85;

    public Task<byte[]?> ExtractAsync(string varPath, string contentEntryPath, CancellationToken cancellationToken = default) =>
        ExtractAsync(varPath, contentEntryPath, MaxDimension, cancellationToken);

    public async Task<byte[]?> ExtractAsync(
        string varPath,
        string contentEntryPath,
        int maxDimension,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(varPath);
        Guard.NotNullOrWhiteSpace(contentEntryPath);
        if (!File.Exists(varPath))
            return null;

        var siblingJpg = PreviewRules.SiblingJpgPath(contentEntryPath);
        try
        {
            using var archive = ZipFile.OpenRead(varPath);
            var entry = archive.GetEntry(siblingJpg) ?? FindIgnoreCase(archive, siblingJpg);
            if (entry is null)
                return null;
            if (entry.Length > IngestLimits.MaxPreviewCompressedBytes)
                return null;

            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > IngestLimits.MaxPreviewCompressedBytes)
                    return null;
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            var raw = memory.ToArray();
            return Downscale(raw, maxDimension, maxDimension > MaxDimension ? FocusJpegQuality : JpegQuality);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resize a preview to fit within <paramref name="maxDimension"/> and re-encode as JPEG.
    /// Undecodable / already-small images keep original bytes when possible.
    /// </summary>
    public static byte[]? Downscale(byte[] jpeg, int maxDimension = MaxDimension, int quality = JpegQuality)
    {
        try
        {
            using var encoded = new SKMemoryStream(jpeg);
            using var codec = SKCodec.Create(encoded);
            if (codec is null)
                return jpeg;
            var sourceInfo = codec.Info;
            var pixels = (long)sourceInfo.Width * sourceInfo.Height;
            if (sourceInfo.Width <= 0 || sourceInfo.Height <= 0 || pixels > IngestLimits.MaxPreviewPixels)
                return null;

            using var bitmap = SKBitmap.Decode(codec);
            if (bitmap is null)
                return jpeg;

            var longest = Math.Max(bitmap.Width, bitmap.Height);
            if (longest <= maxDimension)
                return jpeg;

            var scale = (float)maxDimension / longest;
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                Math.Max(1, (int)Math.Round(bitmap.Height * scale)));

            using var resized = bitmap.Resize(info, SKFilterQuality.Medium);
            if (resized is null)
                return jpeg;
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            var thumb = data.ToArray();
            return thumb.Length > 0 && thumb.Length < jpeg.Length ? thumb : jpeg;
        }
        catch (Exception)
        {
            return jpeg;
        }
    }

    private static ZipArchiveEntry? FindIgnoreCase(ZipArchive archive, string name)
    {
        foreach (var e in archive.Entries)
        {
            if (string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }
}
