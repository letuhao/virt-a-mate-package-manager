using System.IO;
using System.IO.Compression;
using SkiaSharp;
using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Indexing;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Extracts the sibling <c>.jpg</c> preview for a content entry inside a var (e.g. the preview beside a
/// scene JSON) and <b>downscales it to a gallery thumbnail</b> so the packed cache stays small and grid
/// rendering stays fast at 700k-item scale (VaM previews are often 1024²+ and 100KB–1MB raw; a 384px
/// re-encode is ~15–40KB). Returns null when there is no sibling image (→ the caller uses a placeholder).
/// (Checklist 1.32; scale review — downscale-on-extract.)
/// </summary>
public sealed class PreviewExtractor
{
    /// <summary>Longest-edge cap for a stored thumbnail. Big enough for a crisp gallery card, small enough to keep the cache lean.</summary>
    public const int MaxDimension = 384;
    private const int JpegQuality = 80;

    public async Task<byte[]?> ExtractAsync(string varPath, string contentEntryPath, CancellationToken cancellationToken = default)
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

            await using var stream = entry.Open();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            var raw = memory.ToArray();
            return Downscale(raw);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resize a preview to fit within <see cref="MaxDimension"/> and re-encode as JPEG. If the image can't be
    /// decoded, or is already within the cap, the original bytes are returned unchanged (never lose a preview to a
    /// resize failure). Pure/allocating; safe to call on the indexing background thread.
    /// </summary>
    public static byte[]? Downscale(byte[] jpeg)
    {
        try
        {
            // Inspect dimensions before decoding pixels. SKBitmap.Decode(byte[]) allocates the full
            // uncompressed bitmap first, so a tiny compressed image with extreme dimensions could
            // otherwise consume hundreds of MB before the configured cap was checked.
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
                return jpeg; // undecodable (or not really an image) → keep the original bytes

            var longest = Math.Max(bitmap.Width, bitmap.Height);
            if (longest <= MaxDimension)
                return jpeg; // already thumbnail-sized — don't re-encode (avoids a needless quality loss)

            var scale = (float)MaxDimension / longest;
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                Math.Max(1, (int)Math.Round(bitmap.Height * scale)));

            using var resized = bitmap.Resize(info, SKFilterQuality.Medium);
            if (resized is null)
                return jpeg;
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
            var thumb = data.ToArray();

            // Guard: if re-encoding somehow grew the file, keep the smaller original.
            return thumb.Length > 0 && thumb.Length < jpeg.Length ? thumb : jpeg;
        }
        catch (Exception)
        {
            return jpeg; // any Skia failure → keep the original preview rather than dropping it
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
