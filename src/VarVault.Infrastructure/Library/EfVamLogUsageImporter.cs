using Microsoft.EntityFrameworkCore;
using VarVault.Common;
using VarVault.Domain.Analyzer;
using VarVault.Domain.Dependencies;
using VarVault.Domain.Entities;
using VarVault.Domain.Identity;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;

namespace VarVault.Infrastructure.Library;

/// <summary>Resolves VaM log identity tokens against the catalog and records Load / VamLogImport events.</summary>
public sealed class EfVamLogUsageImporter(VarVaultDbContext db, IUsageAnalyzer usage, IClock clock) : IVamLogUsageImporter
{
    public async Task<VamLogUsagePreview> PreviewAsync(string logText, CancellationToken cancellationToken = default)
    {
        var (matched, unmatched) = await ResolveAsync(logText, cancellationToken).ConfigureAwait(false);
        return new VamLogUsagePreview(
            matched.Count + unmatched,
            matched.Count,
            unmatched,
            matched.Select(m => m.VarName).Take(12).ToList());
    }

    public async Task<VamLogUsageImportResult> ImportAsync(string logText, CancellationToken cancellationToken = default)
    {
        var (matched, unmatched) = await ResolveAsync(logText, cancellationToken).ConfigureAwait(false);
        if (matched.Count == 0)
            return new VamLogUsageImportResult(0, 0, unmatched);

        var ids = matched.Select(m => m.PackageId).Distinct().ToList();
        await usage.RecordManyAsync(
            ids,
            UsageKind.Load,
            UsageSource.VamLogImport,
            clock.UtcNow,
            cancellationToken).ConfigureAwait(false);
        var recomputed = await usage.RecomputePackagesAsync(ids, cancellationToken).ConfigureAwait(false);
        return new VamLogUsageImportResult(ids.Count, recomputed, unmatched);
    }

    private sealed record Match(long PackageId, string VarName);

    private async Task<(List<Match> Matched, int Unmatched)> ResolveAsync(string logText, CancellationToken ct)
    {
        var refs = VamLogParser.ExtractUsageRefs(logText);
        var matched = new List<Match>();
        var unmatched = 0;
        foreach (var dep in refs)
        {
            ct.ThrowIfCancellationRequested();
            var hit = await ResolvePackageAsync(dep, ct).ConfigureAwait(false);
            if (hit is null)
                unmatched++;
            else
                matched.Add(hit);
        }
        return (matched, unmatched);
    }

    private async Task<Match?> ResolvePackageAsync(DependencyRef dep, CancellationToken ct)
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
            .Select(p => new Match(p.Id, p.VarName))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }
}
