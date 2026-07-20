namespace VarVault.Domain.Entities;

/// <summary>A persisted import run for the History view. (doc 30 §8; doc 31 Phase 5.6.)</summary>
public sealed class ImportRunEntity
{
    public Guid Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public Guid TargetRepositoryId { get; set; }
    public string SourceSummary { get; set; } = string.Empty;
    public int Copied { get; set; }
    public int Fixed { get; set; }
    public int Renamed { get; set; }
    public int Skipped { get; set; }
    public int Discarded { get; set; }
    public int Failed { get; set; }

    /// <summary>Sources that couldn't be imported (password/corrupt archive) — surfaced for manual handling.</summary>
    public ICollection<ImportFailedSourceEntity> FailedSources { get; } = new List<ImportFailedSourceEntity>();

    /// <summary>Per-item results of the run (one row per applied item). (doc 30 §8.)</summary>
    public ICollection<ImportOutcomeEntity> Outcomes { get; } = new List<ImportOutcomeEntity>();
}

/// <summary>One applied item's outcome within an import run. (doc 30 §8 · ImportOutcomeEntity.)</summary>
public sealed class ImportOutcomeEntity
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public ImportRunEntity? Run { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string IdentityKey { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;   // "ok" | "failed"
    public string? Reason { get; set; }
}

/// <summary>A source that failed during an import run (password-protected / corrupt archive). (doc 30 §8.)</summary>
public sealed class ImportFailedSourceEntity
{
    public long Id { get; set; }
    public Guid RunId { get; set; }
    public ImportRunEntity? Run { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
