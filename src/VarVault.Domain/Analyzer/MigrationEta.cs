namespace VarVault.Domain.Analyzer;

/// <summary>Estimates migration time from bytes ÷ target write speed. (Checklist BE-P4.)</summary>
public static class MigrationEta
{
    public static TimeSpan Estimate(long bytes, double targetWriteMBps)
    {
        if (bytes <= 0 || targetWriteMBps <= 0)
            return TimeSpan.Zero;
        var megabytes = bytes / (1024.0 * 1024.0);
        return TimeSpan.FromSeconds(megabytes / targetWriteMBps);
    }
}
