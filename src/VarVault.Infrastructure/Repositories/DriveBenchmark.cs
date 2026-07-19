using System.Diagnostics;
using System.IO;
using VarVault.Common;
using VarVault.Domain.Repositories;

namespace VarVault.Infrastructure.Repositories;

/// <summary>
/// Sequential read/write benchmark: writes a temp file with WriteThrough (real write speed, not cache),
/// then reads it back, timing both. A rough figure for tier ranking, not a storage-review-grade number.
/// (BE-R1, 1.5.)
/// </summary>
public sealed class DriveBenchmark : IDriveBenchmark
{
    private const int SampleBytes = 24 * 1024 * 1024; // 24 MB — big enough to time, small enough to be quick
    private const int BufferSize = 1 << 20;

    public async Task<DriveBenchmarkResult> MeasureAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(directoryPath);
        Directory.CreateDirectory(directoryPath);
        var path = Path.Combine(directoryPath, $".varvault-bench-{Guid.NewGuid():N}.tmp");

        var buffer = new byte[BufferSize];
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)(i * 7 + 1);

        try
        {
            // Warm-up write is folded into the timed write; the sample size dominates any fixed cost.
            var writeSw = Stopwatch.StartNew();
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                var written = 0;
                while (written < SampleBytes)
                {
                    await output.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                    written += buffer.Length;
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            writeSw.Stop();

            var readSw = Stopwatch.StartNew();
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                while (await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) > 0)
                {
                }
            }
            readSw.Stop();

            return new DriveBenchmarkResult(
                ReadMBps: MegabytesPerSecond(SampleBytes, readSw.Elapsed),
                WriteMBps: MegabytesPerSecond(SampleBytes, writeSw.Elapsed));
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* best effort */ }
        }
    }

    private static double MegabytesPerSecond(long bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(elapsed.TotalSeconds, 0.0001);
        return bytes / (1024.0 * 1024.0) / seconds;
    }
}
