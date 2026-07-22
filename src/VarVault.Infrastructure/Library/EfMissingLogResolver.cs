using Microsoft.EntityFrameworkCore;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Activation;
using VarVault.Sdk.Library;
using VarVault.Sdk.Presets;

namespace VarVault.Infrastructure.Library;

/// <summary>
/// Resolves the packages a pasted VaM error log complains about (<see cref="VamLogParser"/>) against the whole
/// library — dereferencing <c>.latest</c> to the newest version we actually hold — then adds the found set
/// <b>plus its forward-dependency closure</b> into the <b>active</b> loading preset / VaM profile (never a
/// throwaway "VaM Log Repair" preset). That closure is the fix for the old importer that never pulled
/// dependencies. (QoL log-repair.)
/// </summary>
public sealed class EfMissingLogResolver(
    VarVaultDbContext db,
    IPresetService presets,
    IActivationService activation,
    IProfileService profiles)
    : IMissingLogResolver
{
    private readonly ActivePresetActivationHelper _activator = new(db, presets, activation, profiles);

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
        IQueryable<Package> q;
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

    public Task<MissingLogActivation> ActivateAsync(IReadOnlyList<string> varNames, CancellationToken cancellationToken = default) =>
        _activator.ActivateAsync(varNames, cancellationToken);
}
