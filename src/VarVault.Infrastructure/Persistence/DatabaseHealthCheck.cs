using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VarVault.Infrastructure.Persistence;

/// <summary>Readiness probe: the catalog database is reachable.</summary>
internal sealed class DatabaseHealthCheck(VarVaultDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy("database reachable")
                : HealthCheckResult.Unhealthy("database unreachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database error", ex);
        }
    }
}
