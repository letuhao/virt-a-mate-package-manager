namespace VarVault.Domain.Entities;

/// <summary>
/// Materialized read table (not a VIEW) with denormalized aggregates the gallery binds to.
/// Refreshed per-event by the recompute pipeline. PK is <see cref="PackageId"/>. (Data-arch §3, §5.8.)
/// </summary>
public sealed class PackageListItem
{
    public long PackageId { get; set; }
    public Package? Package { get; set; }

    public string VarName { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string VersionToken { get; set; } = string.Empty;

    public ContentType PrimaryType { get; set; } = ContentType.Unknown;
    public long TotalSize { get; set; }

    public int OnlineInstanceCount { get; set; }
    public int TotalInstanceCount { get; set; }

    /// <summary>Derived from <see cref="OnlineInstanceCount"/> — a hard gate for all bulk/auto deletes.</summary>
    public bool IsSingleCopy { get; set; }
    public bool IsFavorite { get; set; }

    public int? ActualTierMin { get; set; }
    public ContentClass Class { get; set; } = ContentClass.Cold;
    public bool IsActive { get; set; }

    /// <summary>When the package was linked into the active loading profile (null when not installed).</summary>
    public DateTime? InstalledAt { get; set; }

    /// <summary>Direct-only, materialized bit — never computed transitively per grid row.</summary>
    public bool HasMissingDeps { get; set; }

    public string? PreviewThumbRef { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>An item held in trash. A per-item manifest inside the trash lets restore survive DB loss. (Data-arch §5.9.)</summary>
public sealed class TrashItem
{
    public long Id { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string TrashPath { get; set; } = string.Empty;

    /// <summary>Nullable — the package may already be gone; ON DELETE SET NULL.</summary>
    public long? PackageId { get; set; }
    public Package? Package { get; set; }

    public string Reason { get; set; } = string.Empty;
    public DateTime TrashedAt { get; set; }
    public long Bytes { get; set; }
}

/// <summary>A key/value app setting (VaM path, tiers, policies, thresholds). Key is the PK.</summary>
public sealed class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
