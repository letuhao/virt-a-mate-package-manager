using System.IO;
using System.Text.Json;
using VarVault.Common;
using VarVault.Domain.Safety;

namespace VarVault.Infrastructure.Safety;

/// <summary>
/// Filesystem trash: moves a file into <c>{trashRoot}/{id}/</c> alongside a <c>manifest.json</c>, so a
/// restore needs only the trash folder — never the catalog DB. Move (not copy+delete) keeps it instant
/// on the same volume. (Data-arch §5.9; checklist X.1/X.2/X.3.)
/// </summary>
public sealed class FileTrashService(string trashRoot, IClock clock, long quotaBytes = long.MaxValue) : ITrashService
{
    private const string ManifestName = "manifest.json";

    public async Task<Result<TrashEntry>> TrashAsync(string sourcePath, string reason, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(sourcePath);
        Guard.NotNull(reason);
        if (!File.Exists(sourcePath))
            return Result.Failure<TrashEntry>("trash.missing", $"File not found: {sourcePath}");

        var id = Guid.NewGuid().ToString("N");
        var itemDir = Path.Combine(trashRoot, id);
        Directory.CreateDirectory(itemDir);

        var fileName = Path.GetFileName(sourcePath);
        var trashPath = Path.Combine(itemDir, fileName);
        var bytes = new FileInfo(sourcePath).Length;

        try
        {
            File.Move(sourcePath, trashPath); // never a hard delete (X.1)
        }
        catch (IOException ex)
        {
            TryCleanup(itemDir);
            return Result.Failure<TrashEntry>("trash.io", ex.Message);
        }

        var entry = new TrashEntry(id, Path.GetFullPath(sourcePath), trashPath, reason, clock.UtcNow.UtcDateTime, bytes);
        await WriteManifestAsync(itemDir, entry, cancellationToken).ConfigureAwait(false);

        await EnforceQuotaAsync(cancellationToken).ConfigureAwait(false);
        return entry;
    }

    // Keep trash under the quota by purging the oldest items (already-deleted files) — never a silent
    // hard-delete of the *source*; only long-trashed items are aged out. (X.2)
    private async Task EnforceQuotaAsync(CancellationToken cancellationToken)
    {
        if (quotaBytes == long.MaxValue)
            return;

        var items = (await ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(e => e.TrashedAtUtc)
            .ToList();

        var total = items.Sum(e => e.Bytes);
        foreach (var oldest in items)
        {
            if (total <= quotaBytes)
                break;
            TryCleanup(Path.Combine(trashRoot, oldest.Id));
            total -= oldest.Bytes;
        }
    }

    public async Task<Result> RestoreAsync(string trashId, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(trashId);
        var manifestPath = Path.Combine(trashRoot, trashId, ManifestName);
        return await RestoreFromManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result> RestoreFromManifestAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(manifestPath);
        if (!File.Exists(manifestPath))
            return Result.Failure("trash.restore.manifest", $"Manifest not found: {manifestPath}");

        TrashEntry? entry;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            entry = await JsonSerializer.DeserializeAsync<TrashEntry>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return Result.Failure("trash.restore.manifest", ex.Message);
        }

        if (entry is null)
            return Result.Failure("trash.restore.manifest", "empty manifest");
        if (!File.Exists(entry.TrashPath))
            return Result.Failure("trash.restore.gone", $"Trashed file is gone: {entry.TrashPath}");
        if (File.Exists(entry.OriginalPath))
            return Result.Failure("trash.restore.occupied", $"Something already exists at {entry.OriginalPath}");

        try
        {
            var dir = Path.GetDirectoryName(entry.OriginalPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Move(entry.TrashPath, entry.OriginalPath); // back to original path (X.3)
            TryCleanup(Path.GetDirectoryName(manifestPath)!);
            return Result.Success();
        }
        catch (IOException ex)
        {
            return Result.Failure("trash.restore.io", ex.Message);
        }
    }

    public async Task<IReadOnlyList<TrashEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<TrashEntry>();
        if (!Directory.Exists(trashRoot))
            return entries;

        foreach (var manifest in Directory.EnumerateFiles(trashRoot, ManifestName, SearchOption.AllDirectories))
        {
            try
            {
                await using var stream = File.OpenRead(manifest);
                var entry = await JsonSerializer.DeserializeAsync<TrashEntry>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch (JsonException) { /* skip corrupt manifest */ }
        }
        return entries;
    }

    public Task<Result> PurgeAsync(string trashId, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(trashId);
        var itemDir = Path.Combine(trashRoot, trashId);
        if (!Directory.Exists(itemDir))
            return Task.FromResult(Result.Failure("trash.purge.missing", $"Trash item not found: {trashId}"));
        try
        {
            Directory.Delete(itemDir, recursive: true); // hard-delete the trashed copy + manifest
            return Task.FromResult(Result.Success());
        }
        catch (IOException ex)
        {
            return Task.FromResult(Result.Failure("trash.purge.io", ex.Message));
        }
    }

    private static async Task WriteManifestAsync(string itemDir, TrashEntry entry, CancellationToken cancellationToken)
    {
        var path = Path.Combine(itemDir, ManifestName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
