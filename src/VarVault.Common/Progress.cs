namespace VarVault.Common;

/// <summary>A unit of progress for a long-running operation (indexing, migration, fixing).</summary>
public readonly record struct ProgressReport(long Done, long Total, string? Message = null)
{
    public double Fraction => Total > 0 ? (double)Done / Total : 0d;
    public bool IsIndeterminate => Total <= 0;
}

/// <summary>
/// Sink for progress + status of a background operation. The standard reporting channel;
/// a no-op default lets callers omit it.
/// </summary>
public interface IProgressSink
{
    void Report(ProgressReport report);

    public static IProgressSink Null { get; } = new NullSink();

    private sealed class NullSink : IProgressSink
    {
        public void Report(ProgressReport report) { }
    }
}
