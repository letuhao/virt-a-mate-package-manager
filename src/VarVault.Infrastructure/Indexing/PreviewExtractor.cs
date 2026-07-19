using System.IO;
using System.IO.Compression;
using VarVault.Common;
using VarVault.Domain.Content;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Extracts the sibling <c>.jpg</c> preview for a content entry inside a var (e.g. the preview beside a
/// scene JSON). Returns null when there is no sibling image (→ the caller uses a type placeholder).
/// (Checklist 1.32.)
/// </summary>
public sealed class PreviewExtractor
{
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
            return memory.ToArray();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
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
