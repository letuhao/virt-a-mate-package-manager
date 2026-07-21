namespace VarVault.Domain.Entities;

/// <summary>
/// Logical var identity, anchored on the verbatim base filename <c>Creator.Package.Version</c>.
/// One Package → many <see cref="VarFile"/> copies. Holds favorites/tags/usage/presets and
/// dependency resolution. (Data-arch §2, §3 Catalog.)
/// </summary>
public sealed class Package
{
    public long Id { get; set; }

    /// <summary>Verbatim <c>Creator.Package.Version</c> (e.g. <c>.007</c> preserved). Unique.</summary>
    public string VarName { get; set; } = string.Empty;

    /// <summary>NFC + case-fold match key. Unique. All joins/matching use this; display uses <see cref="VarName"/>.</summary>
    public string IdentityKey { get; set; } = string.Empty;

    // Facets — parsed from the filename, never meta.json.
    public string Creator { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string VersionToken { get; set; } = string.Empty;
    public long VersionSort { get; set; }

    // meta.json's own names, stored for divergence reporting only (never identity).
    public string? MetaCreator { get; set; }
    public string? MetaPackage { get; set; }
    public bool MetaDivergent { get; set; }

    public string? LicenseType { get; set; }
    public string? Description { get; set; }
    public string? ProgramVersion { get; set; }
    public DateTime? MetaDate { get; set; }

    public bool IsFavorite { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastIndexedAt { get; set; }

    /// <summary>Direct in-degree, maintained incrementally (never enumerated on the interactive path).</summary>
    public int ReverseDependentCount { get; set; }
    public bool IsFoundational { get; set; }

    /// <summary>Elected canonical copy. Nullable; ON DELETE SET NULL — losing the copy never deletes the Package.</summary>
    public long? CanonicalVarFileId { get; set; }
    public VarFile? CanonicalVarFile { get; set; }

    public ICollection<VarFile> VarFiles { get; } = new List<VarFile>();
}

/// <summary>One physical <c>.var</c> file: a path in a repo plus its physical facts and fingerprints. (Data-arch §3.)</summary>
public sealed class VarFile
{
    public long Id { get; set; }

    /// <summary>Owning logical package; nullable for names that don't parse (Unrecognized bucket).</summary>
    public long? PackageId { get; set; }
    public Package? Package { get; set; }

    public Guid RepositoryId { get; set; }
    public Repository? Repository { get; set; }

    /// <summary>Path relative to the repository mount; unique within a repository.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public DateTime FileMtime { get; set; }

    /// <summary>Cheap freshness hash (size + head/tail + central-dir offset/count).</summary>
    public string? QuickHash { get; set; }

    /// <summary>Primary dedup key: hash of sorted (raw entry-name bytes, uncompressed size, CRC-32) multiset.</summary>
    public string? ContentSignature { get; set; }

    /// <summary>As ContentSignature but excluding meta.json (near-dup detection).</summary>
    public string? PayloadSignature { get; set; }

    /// <summary>Size+CRC multiset with paths excluded — relates an encoding-fixed var to its original.</summary>
    public string? ContentSignatureNoPath { get; set; }

    /// <summary>Full SHA-256; lazy — computed only for verify-before-delete and portability re-match.</summary>
    public string? ContentHash { get; set; }

    public EncodingHealth EncodingHealth { get; set; } = EncodingHealth.Unknown;
    public string? DetectedCodepage { get; set; }
    public int BrokenEntryCount { get; set; }
    public IntegrityStatus IntegrityStatus { get; set; } = IntegrityStatus.Ok;

    /// <summary>Fix lineage: the broken original this file was produced from.</summary>
    public long? FixedFromVarFileId { get; set; }
    public VarFile? FixedFromVarFile { get; set; }

    public long? SupersededByVarFileId { get; set; }
    public VarFile? SupersededByVarFile { get; set; }

    public QuarantineKind QuarantineKind { get; set; } = QuarantineKind.None;
    public DateTime IndexedAt { get; set; }

    /// <summary>Durable ingest completeness for crash-resume (A15). Freshness skip only when RawStored+fresh.</summary>
    public IngestState IngestState { get; set; } = IngestState.Discovered;

    /// <summary>Scan generation that last saw this file during discovery.</summary>
    public long SeenGeneration { get; set; }

    /// <summary>Worker lease owner; null when unclaimed.</summary>
    public Guid? LeaseOwner { get; set; }

    /// <summary>UTC expiry of the current inspect lease.</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    public int IngestAttempts { get; set; }
    public string? IngestError { get; set; }

    public ICollection<ContentItem> ContentItems { get; } = new List<ContentItem>();
    public ICollection<Dependency> Dependencies { get; } = new List<Dependency>();
}

/// <summary>One content entry inside a var — keyed on <see cref="VarFileId"/> (content is physical). (Data-arch §3.)</summary>
public sealed class ContentItem
{
    public long Id { get; set; }
    public long VarFileId { get; set; }
    public VarFile? VarFile { get; set; }

    public ContentType Type { get; set; } = ContentType.Unknown;
    public string EntryPath { get; set; } = string.Empty;
    public bool IsPreset { get; set; }
    public string? PreviewThumbRef { get; set; }
    public Gender? Gender { get; set; }
    public double? GenderConfidence { get; set; }
}

/// <summary>Per-type content counts for a package, derived from its canonical var. Normalized (no schema churn per type).</summary>
public sealed class PackageContentCount
{
    public long PackageId { get; set; }
    public Package? Package { get; set; }
    public ContentType Type { get; set; }
    public int Count { get; set; }
}
