using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using VarVault.Common.Diagnostics;
using VarVault.Sdk.Events;
using VarVault.Sdk.Modularity;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end over the composed host: events → handlers, background jobs, single-writer
/// commits, health checks, metrics, and captured logs all wired together.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public class FoundationFlowTests
{
    [Fact]
    public async Task Host_runs_events_jobs_writes_and_reports_health_metrics_and_logs()
    {
        PingHandler.Count = 0;
        await using var app = TestHost.Create(withPersistence: true, extraModules: new PingModule());
        using var jobMetrics = new MetricCollector<long>(Telemetry.JobsCompleted);

        // 1) event → DI-registered handler
        await app.Get<IEventBus>().PublishAsync(new PingEvent());
        Assert.Equal(1, PingHandler.Count);

        // 2) background job runs to completion, emits a metric
        var ran = new TaskCompletionSource();
        app.Get<IJobQueue>().Enqueue("e2e", _ => { ran.SetResult(); return Task.CompletedTask; });
        await ran.Task;
        for (var i = 0; i < 100 && jobMetrics.GetMeasurementSnapshot().Count == 0; i++)
            await Task.Delay(10);
        Assert.True(jobMetrics.GetMeasurementSnapshot().Count >= 1, "jobs.completed metric should have fired");

        // 3) single-writer commit returns its result
        var result = await app.Get<IWriteQueue>().EnqueueAsync(_ => Task.FromResult(7));
        Assert.Equal(7, result);

        // 4) health checks (write-queue liveness + database readiness) are healthy
        var report = await app.Get<HealthCheckService>().CheckHealthAsync();
        var detail = string.Join("; ", report.Entries.Select(e => $"{e.Key}={e.Value.Status}:{e.Value.Description}:{e.Value.Exception?.Message}"));
        Assert.True(report.Status == HealthStatus.Healthy, detail);
        Assert.Contains("database", report.Entries.Keys);
        Assert.Contains("write-queue", report.Entries.Keys);

        // 5) structured startup log was captured
        Assert.Contains(app.Logs.Entries, e => e.Message.Contains("host composed", StringComparison.Ordinal));
    }

    private sealed record PingEvent : IDomainEvent;

    private sealed class PingHandler : IEventHandler<PingEvent>
    {
        public static int Count;
        public Task HandleAsync(PingEvent @event, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return Task.CompletedTask;
        }
    }

    private sealed class PingModule : IModule
    {
        public string Name => "Ping";
        public void Register(IServiceCollection services, IModuleContext context) =>
            services.AddSingleton<IEventHandler<PingEvent>, PingHandler>();
    }
}
