using System.IO;
using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Scans a user's loose scenes/looks/presets (under <c>Saves</c> / <c>Custom</c>) for embedded package
/// references and records <see cref="UserSave"/> + <see cref="SaveDependency"/> edges (resolved against
/// the catalog). These edges make a var needed only by the user's own content non-orphan. (Checklist 2.13.)
/// </summary>
public sealed class UserSaveScanner(VarVaultDbContext db, IClock clock)
{
    private static readonly string[] Extensions = [".json", ".vap", ".vac", ".scene"];

    public async Task<int> ScanAsync(string savesRoot, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(savesRoot);
        if (!Directory.Exists(savesRoot))
            return 0;

        var families = await BuildFamilyMapAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow.UtcDateTime;
        var scanned = 0;

        foreach (var file in Directory.EnumerateFiles(savesRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            string text;
            try { text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { continue; }

            var refs = EmbeddedRefExtractor.Extract(text);

            var fullPath = Path.GetFullPath(file);
            var save = await db.UserSaves.Include(u => u.Dependencies)
                .FirstOrDefaultAsync(u => u.Path == fullPath, cancellationToken).ConfigureAwait(false);
            if (save is null)
            {
                save = new UserSave { Path = fullPath };
                db.UserSaves.Add(save);
            }
            save.Type = Path.GetExtension(file).TrimStart('.');
            save.Mtime = File.GetLastWriteTimeUtc(file);
            save.LastScannedAt = now;

            if (save.Dependencies.Count > 0)
                db.SaveDependencies.RemoveRange(save.Dependencies);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in refs)
            {
                var parsed = DependencyRef.Parse(raw);
                if (parsed.IsFailure)
                    continue;
                var reference = parsed.Value;
                var key = IdentityFold.Compute(raw);
                if (!seen.Add(key))
                    continue;

                long? resolved = null;
                if (families.TryGetValue(reference.FamilyKey, out var available))
                    resolved = VersionResolver.Resolve(reference, available)?.PackageId;

                db.SaveDependencies.Add(new SaveDependency
                {
                    UserSave = save,
                    DependsOnRefKey = key,
                    DependsOnRefRaw = raw,
                    ResolvedPackageId = resolved,
                    IsMissing = resolved is null,
                });
            }

            scanned++;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return scanned;
    }

    private async Task<Dictionary<string, List<AvailableVersion>>> BuildFamilyMapAsync(CancellationToken cancellationToken)
    {
        var packages = await db.Packages
            .Select(p => new { p.Id, p.Creator, p.PackageName, p.VersionSort })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var map = new Dictionary<string, List<AvailableVersion>>(StringComparer.Ordinal);
        foreach (var p in packages)
        {
            var family = IdentityFold.Compute($"{p.Creator}.{p.PackageName}");
            if (!map.TryGetValue(family, out var list))
                map[family] = list = [];
            list.Add(new AvailableVersion(p.VersionSort, p.Id));
        }
        return map;
    }
}
