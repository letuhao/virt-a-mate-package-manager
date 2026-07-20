using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Resolves the packages a pasted VaM error log complains about (<see cref="VamLogParser"/>) against the whole
/// library — dereferencing <c>.latest</c> to the newest version we actually hold — then activates the found set
/// <b>plus its forward-dependency closure</b> into the active VaM profile via the preset/activation flow. That
/// closure is the fix for the old importer that never pulled dependencies. (QoL log-repair.)
/// </summary>
public sealed class EfMissingLogResolver(
    VarVaultDbContext db, IDependencyGraph graph, IPresetService presets, IActivationService activation)
    : IMissingLogResolver
{
    private const string RepairPresetName = "VaM Log Repair";

    public async Task<MissingLogAnalysis> AnalyzeAsync(string logText, CancellationToken cancellationToken = default)
    {
        var refs = VamLogParser.ExtractRefs(logText);
        var entries = new List<MissingLogEntry>(refs.Count);
        foreach (var dep in refs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hit = await ResolveAsync(dep, cancellationToken).ConfigureAwait(false);
            var refText = dep.VersionKind == VersionSpecKind.Latest
                ? $"{dep.Creator}.{dep.Package}.latest"
                : $"{dep.Creator}.{dep.Package}.{dep.ExactVersion}";
            entries.Add(new MissingLogEntry(
                refText, dep.Creator, dep.Package, dep.VersionKind == VersionSpecKind.Latest,
                hit?.VarName, hit is not null, hit?.Tier, hit?.RepositoryName));
        }
        return new MissingLogAnalysis(entries);
    }

    private sealed record Hit(string VarName, int? Tier, string? RepositoryName);

    /// <summary>Match a ref against the catalog by fold key: latest → newest in the family; exact → that identity.
    /// Both keys are folded client-side, then used as plain values EF can translate (LIKE prefix / equality).</summary>
    private async Task<Hit?> ResolveAsync(DependencyRef dep, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Package> q;
        if (dep.VersionKind == VersionSpecKind.Latest)
        {
            var prefix = IdentityFold.Compute($"{dep.Creator}.{dep.Package}") + ".";
            q = db.Packages.AsNoTracking().Where(p => p.IdentityKey.StartsWith(prefix));
        }
        else
        {
            var idk = IdentityFold.Compute($"{dep.Creator}.{dep.Package}.{dep.ExactVersion}");
            q = db.Packages.AsNoTracking().Where(p => p.IdentityKey == idk);
        }

        return await q
            .OrderByDescending(p => p.VersionSort)
            .Select(p => new Hit(
                p.VarName,
                p.VarFiles.OrderByDescending(v => v.Repository!.IsOnline).ThenBy(v => v.Repository!.Tier)
                    .Select(v => (int?)v.Repository!.Tier).FirstOrDefault(),
                p.VarFiles.OrderByDescending(v => v.Repository!.IsOnline).ThenBy(v => v.Repository!.Tier)
                    .Select(v => v.Repository!.Name).FirstOrDefault()))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task<MissingLogActivation> ActivateAsync(IReadOnlyList<string> varNames, CancellationToken cancellationToken = default)
    {
        var members = varNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (members.Count == 0)
            return new MissingLogActivation(0, 0, 0, 0);

        // Replace the repair preset so each run activates exactly the current found set (+ its closure).
        var existing = (await presets.ListAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.Name, RepairPresetName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            await presets.DeleteAsync(existing.Id, cancellationToken).ConfigureAwait(false);

        var created = await presets.CreateAsync(RepairPresetName, members, cancellationToken).ConfigureAwait(false);
        if (created.IsFailure)
            return new MissingLogActivation(0, 0, 0, 0);

        var build = await activation.BuildProfileLinksAsync(created.Value.Id, cancellationToken).ConfigureAwait(false);
        return new MissingLogActivation(
            MembersActivated: created.Value.MemberCount,
            LinksCreated: build.LinksCreated,
            StillMissing: build.MissingPackages,
            PrivilegeFailures: build.PrivilegeFailures);
    }
}
