namespace VarVault.Domain.Repositories;

/// <summary>Measured throughput of a drive (MB/s).</summary>
public sealed record DriveBenchmarkResult(double ReadMBps, double WriteMBps);

/// <summary>
/// Measures a repository drive's sequential read/write throughput by timing a temp file in it. Used to
/// rank drives into tiers (fastest SSD = Tier 1 hot path). Implemented by Infrastructure. (BE-R1, 1.5.)
/// </summary>
public interface IDriveBenchmark
{
    Task<DriveBenchmarkResult> MeasureAsync(string directoryPath, CancellationToken cancellationToken = default);
}
