using Microsoft.Extensions.Diagnostics.HealthChecks;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Diagnostics;

/// <summary>Liveness probe: the write queue drains a no-op write within 2s.</summary>
internal sealed class WriteQueueHealthCheck(IWriteQueue queue) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            await queue.EnqueueAsync(_ => Task.CompletedTask, WritePriority.Interactive, cts.Token).ConfigureAwait(false);
            return HealthCheckResult.Healthy("write queue responsive");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("write queue did not drain within 2s");
        }
    }
}
