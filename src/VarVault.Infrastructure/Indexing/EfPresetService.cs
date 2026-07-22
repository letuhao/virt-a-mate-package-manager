using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Paging;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// EF loading-preset CRUD + activation planning. Members are stored by folded name and re-resolved
/// against the current library before preview so <c>.latest</c> and formerly-missing refs stay fresh;
/// the preview unions the forward-dependency closure of every resolved member. (3.6/3.8; deep closure.)
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
        var skipped = new List<string>();
        foreach (var raw in memberRefs)
        {
            var member = BuildMember(preset.Id, raw, order, families);
            if (member is null)
            {
                skipped.Add(raw);
                continue;
            }
            db.PresetMembers.Add(member);
            order++;
        }
        if (skipped.Count > 0 && order == 0)
        {
            db.LoadingPresets.Remove(preset);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<PresetInfo>("preset.member.parse",
                $"No valid member refs — first invalid: '{skipped[0]}'.");
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await InfoAsync(preset.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PresetInfo>> AddMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return Result.Failure<PresetInfo>("preset.missing", "Preset not found.");

        // Idempotent: (PresetId, PackageRefKey) is unique — re-adding refreshes the resolution snapshot.
        var key = IdentityFold.Compute(memberRef);
        var existing = await db.PresetMembers
            .FirstOrDefaultAsync(m => m.PresetId == presetId && m.PackageRefKey == key, cancellationToken)
            .ConfigureAwait(false);
        var families = await BuildFamilyMapAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            ApplyResolution(existing, memberRef, families);
            preset.UpdatedAt = clock.UtcNow.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return await InfoAsync(presetId, cancellationToken).ConfigureAwait(false);
        }

        var order = await db.PresetMembers.Where(m => m.PresetId == presetId).CountAsync(cancellationToken).ConfigureAwait(false);
        var member = BuildMember(presetId, memberRef, order, families);
        if (member is null)
            return Result.Failure<PresetInfo>("preset.member.parse", $"'{memberRef}' is not a valid ref.");

        db.PresetMembers.Add(member);
        preset.UpdatedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await InfoAsync(presetId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PresetInfo>> RemoveMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default)
    {
        var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        if (preset is null)
            return Result.Failure<PresetInfo>("preset.missing", "Preset not found.");

        var key = IdentityFold.Compute(memberRef);
        var member = await db.PresetMembers
            .FirstOrDefaultAsync(m => m.PresetId == presetId && m.PackageRefKey == key, cancellationToken).ConfigureAwait(false);
        if (member is null)
            return Result.Failure<PresetInfo>("preset.member.missing", $"'{memberRef}' is not a member.");

        db.PresetMembers.Remove(member);
        preset.UpdatedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await InfoAsync(presetId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> MembersAsync(long presetId, CancellationToken cancellationToken = default) =>
        await db.PresetMembers.AsNoTracking()
            .Where(m => m.PresetId == presetId)
            .OrderBy(m => m.SortOrder)
            .Select(m => m.PackageRefRaw)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<PageResult<string>> MembersPageAsync(long presetId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Normalize();
        var query = db.PresetMembers.AsNoTracking().Where(m => m.PresetId == presetId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(m => m.SortOrder)
            .Skip(page.Skip)
            .Take(page.SafePageSize)
            .Select(m => m.PackageRefRaw)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PageResult<string>(items, total, page.SafePageNumber, page.SafePageSize);
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
        if (!await db.LoadingPresets.AnyAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false))
            return null;

        // Re-resolve stored refs so .latest / formerly-missing members reflect the current catalog.
        await RefreshMemberResolutionsAsync(presetId, cancellationToken).ConfigureAwait(false);

        var members = await db.PresetMembers.AsNoTracking()
            .Where(m => m.PresetId == presetId)
            .Select(m => new { m.ResolvedPackageId, m.PackageRefKey, m.PackageRefRaw })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var resolved = new HashSet<long>();
        var unresolvedKeys = new List<(string Key, string Raw)>();
        foreach (var m in members)
        {
            if (m.ResolvedPackageId is { } id)
                resolved.Add(id);
            else
                unresolvedKeys.Add((m.PackageRefKey, m.PackageRefRaw));
        }

        // Same alias rules as activation — preview must not over-report aliased members as missing.
        var missing = new List<string>();
        var directResolved = 0;
        if (unresolvedKeys.Count > 0)
        {
            var keys = unresolvedKeys.Select(u => u.Key).ToList();
            var aliases = await db.VarAliases.AsNoTracking()
                .Where(a => a.ResolvedPackageId != null
                            && keys.Contains(a.MissingRefKey)
                            && (a.Scope == AliasScope.Global || a.PresetId == presetId))
                .Select(a => new { a.MissingRefKey, TargetId = a.ResolvedPackageId!.Value })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var aliased = aliases.Select(a => a.MissingRefKey).ToHashSet(StringComparer.Ordinal);
            foreach (var a in aliases)
                resolved.Add(a.TargetId);
            directResolved = members.Count(m => m.ResolvedPackageId != null) + aliases.Count;
            foreach (var u in unresolvedKeys)
            {
                if (!aliased.Contains(u.Key))
                    missing.Add(u.Raw);
            }
        }
        else
        {
            directResolved = resolved.Count;
        }

        // Union each resolved member/alias-target with its forward closure.
        var full = new HashSet<long>(resolved);
        foreach (var id in resolved.ToList())
        {
            var closure = await graph.ForwardClosureDetailedAsync(id, cancellationToken).ConfigureAwait(false);
            foreach (var dep in closure.PackageIds)
                full.Add(dep);
            foreach (var edge in closure.UnresolvedEdges)
                missing.Add(edge.DependsOnRefRaw);
        }

        return new ActivationPreview(directResolved, full.Count, missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Re-resolve every preset member against the current package family map (`.latest`, substitutions).</summary>
    public async Task RefreshMemberResolutionsAsync(long presetId, CancellationToken cancellationToken = default)
    {
        var members = await db.PresetMembers
            .Where(m => m.PresetId == presetId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (members.Count == 0)
            return;

        var families = await BuildFamilyMapAsync(cancellationToken).ConfigureAwait(false);
        var dirty = false;
        foreach (var member in members)
        {
            var before = member.ResolvedPackageId;
            var beforeSub = member.IsVersionSubstituted;
            ApplyResolution(member, member.PackageRefRaw, families);
            if (member.ResolvedPackageId != before || member.IsVersionSubstituted != beforeSub)
                dirty = true;
        }

        if (dirty)
        {
            var preset = await db.LoadingPresets.FirstOrDefaultAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
            if (preset is not null)
                preset.UpdatedAt = clock.UtcNow.UtcDateTime;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
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

    private static void ApplyResolution(PresetMember member, string raw, Dictionary<string, List<AvailableVersion>> families)
    {
        var parsed = DependencyRef.Parse(raw);
        if (parsed.IsFailure)
        {
            member.ResolvedPackageId = null;
            member.IsVersionSubstituted = false;
            return;
        }

        var reference = parsed.Value;
        member.ResolutionMode = reference.VersionKind == VersionSpecKind.Latest ? ResolutionMode.Latest : ResolutionMode.Exact;
        if (families.TryGetValue(reference.FamilyKey, out var available))
        {
            var resolution = VersionResolver.Resolve(reference, available);
            member.ResolvedPackageId = resolution?.PackageId;
            member.IsVersionSubstituted = resolution?.IsVersionSubstituted ?? false;
        }
        else
        {
            member.ResolvedPackageId = null;
            member.IsVersionSubstituted = false;
        }
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
