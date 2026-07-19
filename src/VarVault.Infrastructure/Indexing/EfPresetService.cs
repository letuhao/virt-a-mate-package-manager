using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF loading-preset CRUD + activation planning. Members are stored by folded name and resolved to
/// packages; the preview unions the forward-dependency closure of every resolved member. (3.6/3.8.)
/// </summary>
public sealed class EfPresetService(VarVaultDbContext db, IClock clock, IDependencyGraph graph) : IPresetService
{
    public async Task<Result<PresetInfo>> CreateAsync(string name, IEnumerable<string> memberRefs, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(name);
        if (await db.LoadingPresets.AnyAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false))
            return Result.Failure<PresetInfo>("preset.duplicate", $"A preset named '{name}' already exists.");

        var now = clock.UtcNow.UtcDateTime;
        var preset = new LoadingPreset { Name = name, CreatedAt = now, UpdatedAt = now };
        db.LoadingPresets.Add(preset);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var families = await BuildFamilyMapAsync(cancellationToken).ConfigureAwait(false);
        var order = 0;
        foreach (var raw in memberRefs)
        {
            var member = BuildMember(preset.Id, raw, order++, families);
            if (member is not null)
                db.PresetMembers.Add(member);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await InfoAsync(preset.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PresetInfo>> AddMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return Result.Failure<PresetInfo>("preset.missing", "Preset not found.");

        var order = await db.PresetMembers.Where(m => m.PresetId == presetId).CountAsync(cancellationToken).ConfigureAwait(false);
        var families = await BuildFamilyMapAsync(cancellationToken).ConfigureAwait(false);
        var member = BuildMember(presetId, memberRef, order, families);
        if (member is null)
            return Result.Failure<PresetInfo>("preset.member.parse", $"'{memberRef}' is not a valid ref.");

        db.PresetMembers.Add(member);
        preset.UpdatedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await InfoAsync(presetId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PresetInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.LoadingPresets.AsNoTracking()
            .Select(p => new PresetInfo(p.Id, p.Name, p.Members.Count))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> DeleteAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return false;
        db.LoadingPresets.Remove(preset); // cascades members
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<ActivationPreview?> PreviewActivationAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var members = await db.PresetMembers.AsNoTracking()
            .Where(m => m.PresetId == presetId)
            .Select(m => new { m.ResolvedPackageId, m.PackageRefRaw })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (members.Count == 0 && !await db.LoadingPresets.AnyAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false))
            return null;

        var resolved = members.Where(m => m.ResolvedPackageId != null).Select(m => m.ResolvedPackageId!.Value).ToHashSet();
        var missing = members.Where(m => m.ResolvedPackageId is null).Select(m => m.PackageRefRaw).ToList();

        // Union each resolved member with its forward closure → the full set that would be pulled in.
        var full = new HashSet<long>(resolved);
        foreach (var id in resolved)
        {
            foreach (var dep in await graph.ForwardClosureAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false))
                full.Add(dep);
        }

        return new ActivationPreview(resolved.Count, full.Count, missing);
    }

    private PresetMember? BuildMember(long presetId, string raw, int order, Dictionary<string, List<AvailableVersion>> families)
    {
        var parsed = DependencyRef.Parse(raw);
        if (parsed.IsFailure)
            return null;
        var reference = parsed.Value;

        long? resolvedId = null;
        var substituted = false;
        if (families.TryGetValue(reference.FamilyKey, out var available))
        {
            var resolution = VersionResolver.Resolve(reference, available);
            resolvedId = resolution?.PackageId;
            substituted = resolution?.IsVersionSubstituted ?? false;
        }

        return new PresetMember
        {
            PresetId = presetId,
            PackageRefKey = IdentityFold.Compute(raw),
            PackageRefRaw = raw,
            ResolutionMode = reference.VersionKind == VersionSpecKind.Latest ? ResolutionMode.Latest : ResolutionMode.Exact,
            ResolvedPackageId = resolvedId,
            IsVersionSubstituted = substituted,
            SortOrder = order,
        };
    }

    private async Task<Result<PresetInfo>> InfoAsync(long presetId, CancellationToken cancellationToken)
    {
        var info = await db.LoadingPresets.AsNoTracking()
            .Where(p => p.Id == presetId)
            .Select(p => new PresetInfo(p.Id, p.Name, p.Members.Count))
            .FirstAsync(cancellationToken).ConfigureAwait(false);
        return info;
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
