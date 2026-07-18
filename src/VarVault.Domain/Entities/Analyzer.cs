namespace VarVault.Domain.Entities;

/// <summary>An append-only usage signal. <see cref="TimestampUnixMs"/> is a UTC epoch (ms). (Data-arch §3 Analyzer.)</summary>
public sealed class UsageEvent
{
    public long Id { get; set; }

    public long PackageId { get; set; }
    public Package? Package { get; set; }

    /// <summary>UTC epoch in milliseconds (stored as an integer, never a locale-formatted string).</summary>
    public long TimestampUnixMs { get; set; }

    public UsageKind Kind { get; set; }
    public UsageSource Source { get; set; }
}

/// <summary>Materialized per-package usage/classification state; reproducible from stored inputs after restore.</summary>
public sealed class UsageStat
{
    public long PackageId { get; set; }
    public Package? Package { get; set; }

    public DateTime? LastUsedAt { get; set; }
    public long UseCountTotal { get; set; }

    /// <summary>Windowed counts computed time-relative (recomputed as the clock advances).</summary>
    public int Use30d { get; set; }
    public int Use90d { get; set; }

    public double CentralityScore { get; set; }
    public double Score { get; set; }
    public ContentClass Class { get; set; } = ContentClass.Cold;

    /// <summary>JSON score history so hysteresis (Class flips) is reproducible, not path-dependent.</summary>
    public string? ScoreHistory { get; set; }
    public DateTime? LastFlipAt { get; set; }

    public bool IsPinnedHot { get; set; }
    public bool IsForcedCold { get; set; }
    public DateTime ComputedAt { get; set; }
}

/// <summary>A durable file-migration job with a resumable state machine. One live job per file. (Data-arch §5.6.)</summary>
public sealed class MigrationJob
{
    public long Id { get; set; }

    public long VarFileId { get; set; }
    public VarFile? VarFile { get; set; }

    public Guid SourceRepositoryId { get; set; }
    public Guid TargetRepositoryId { get; set; }

    public MigrationState State { get; set; } = MigrationState.Planned;
    public long BytesCopied { get; set; }
    public string? TempPath { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Error { get; set; }
}
