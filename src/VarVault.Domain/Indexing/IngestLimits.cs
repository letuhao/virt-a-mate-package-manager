using VarVault.Domain.Entities;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Caps for one-handle VAR inspection so a zip bomb / huge scene JSON cannot exhaust RAM.
/// Applied per entry and per inspection. (5 TB redesign.)
/// </summary>
public static class IngestLimits
{
    /// <summary>Max decompressed bytes for meta.json or any embedded JSON/VAP/VAC scanned for refs.</summary>
    public const long MaxJsonEntryBytes = 32 * 1024 * 1024; // 32 MiB

    /// <summary>Max compressed bytes for a representative preview JPEG before decode.</summary>
    public const long MaxPreviewCompressedBytes = 8 * 1024 * 1024; // 8 MiB

    /// <summary>Max pixel count (width×height) allowed when decoding a preview.</summary>
    public const int MaxPreviewPixels = 4096 * 4096;

    /// <summary>Max central-directory size we'll allocate (guards pathological zips).</summary>
    public const int MaxCentralDirectoryBytes = 64 * 1024 * 1024; // 64 MiB

    /// <summary>How long an Inspecting lease lasts before another worker may reclaim it.</summary>
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>Default channel capacity between discovery → inspect → persist (bounds RAM).</summary>
    public const int DefaultPipelineCapacity = 64;

    /// <summary>
    /// Vars persisted per write-queue action (one scope + one lock acquire per batch). Memory-safe by
    /// construction: a batch also flushes early at <see cref="MaxPersistBatchBytes"/>, so N only trades
    /// write-amortization vs. how long one batch holds the writer lock. Tunable via
    /// <c>VARVAULT_PERSIST_BATCH</c> for benchmarking (clamped 1–512).
    /// </summary>
    public static int PersistBatchSize { get; } = ReadBatchSize();

    /// <summary>Byte budget per persist batch — flush early when buffered upserts+thumbs exceed this.</summary>
    public const long MaxPersistBatchBytes = 8 * 1024 * 1024; // 8 MiB

    /// <summary>Capacity of the inspected→persist channel; with the batch buffer this caps pipeline RAM.</summary>
    public const int PendingChannelCapacity = 16;

    /// <summary>Discovery rows per transaction; paths are also bounded by the byte budget below.</summary>
    public const int DiscoveryBatchSize = 128;

    /// <summary>Approximate in-memory path budget for one discovery transaction.</summary>
    public const long MaxDiscoveryBatchBytes = 1024 * 1024; // 1 MiB

    private static int ReadBatchSize()
    {
        var raw = Environment.GetEnvironmentVariable("VARVAULT_PERSIST_BATCH");
        // Real 420-VAR sweep (HDD): 16 had the best wall time and much shorter p95 writer-lock
        // hold than 32/64, while keeping the pending payload below 0.6 MiB.
        return int.TryParse(raw, out var n) ? Math.Clamp(n, 1, 512) : 16;
    }

    /// <summary>Hard stop so a permanently bad VAR cannot be claimed forever across reclaim cycles.</summary>
    public const int MaxIngestAttempts = 3;

    public static int DegreeFor(MediaType mediaType) => ParallelismPolicy.DegreeFor(mediaType);
}
