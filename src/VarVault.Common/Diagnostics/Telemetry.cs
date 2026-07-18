using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VarVault.Common.Diagnostics;

/// <summary>
/// The single, reused <see cref="ActivitySource"/> and <see cref="Meter"/> for the app,
/// plus the metric instruments. Observing code (a listener, an OpenTelemetry exporter, or
/// a test's <c>MetricCollector</c>) subscribes to the "VarVault" source/meter. Tracing +
/// metrics are the app's monitoring surface.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "VarVault";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);

    // Write queue (single-writer catalog mutations)
    public static readonly Counter<long> WritesCompleted = Meter.CreateCounter<long>("varvault.writes.completed");
    public static readonly Counter<long> WritesFailed = Meter.CreateCounter<long>("varvault.writes.failed");
    public static readonly Histogram<double> WriteDurationMs = Meter.CreateHistogram<double>("varvault.write.duration", unit: "ms");

    // Background jobs
    public static readonly Counter<long> JobsStarted = Meter.CreateCounter<long>("varvault.jobs.started");
    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>("varvault.jobs.completed");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("varvault.jobs.failed");

    /// <summary>Start a traced activity for an operation (no-op if nobody is listening).</summary>
    public static Activity? StartActivity(string name) => ActivitySource.StartActivity(name);
}
