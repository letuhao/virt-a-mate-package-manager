namespace VarVault.Domain.Entities;

/// <summary>Physical medium of a repository's drive; drives tier assignment.</summary>
public enum MediaType
{
    Unknown = 0,
    Nvme = 1,
    Ssd = 2,
    Hdd = 3,
    Network = 4,
    Removable = 5,
}

/// <summary>CJK encoding health of a var's entry names.</summary>
public enum EncodingHealth
{
    Unknown = 0,
    Ok = 1,
    NeedsFix = 2,
    PartiallyBroken = 3,
    Fixed = 4,
}

/// <summary>Structural integrity of a var file.</summary>
public enum IntegrityStatus
{
    Ok = 0,
    CorruptZip = 1,
    MissingMeta = 2,
    BadName = 3,
    /// <summary>Two+ zip members collapse to the same VaM path key (RegisterPackage dictionary crash).</summary>
    DuplicateEntries = 4,
}

/// <summary>Recognized legacy quarantine directories (imported from old varManager).</summary>
public enum QuarantineKind
{
    None = 0,
    Redundant = 1,
    Stale = 2,
    OldVersion = 3,
    Deleted = 4,
}

/// <summary>Where a dependency edge was harvested from.</summary>
public enum RefKind
{
    Meta = 0,
    Embedded = 1,
    Self = 2,
}

/// <summary>How a dependency/preset/alias reference was resolved to a package.</summary>
public enum ResolvedVia
{
    None = 0,
    Exact = 1,
    Latest = 2,
    Closest = 3,
    Alias = 4,
}

/// <summary>Kind of a usage signal.</summary>
public enum UsageKind
{
    Activate = 0,
    Load = 1,
    PresetLoad = 2,
}

/// <summary>Origin of a usage signal.</summary>
public enum UsageSource
{
    AppObserved = 0,
    VamLogImport = 1,
}

/// <summary>Durability state machine for a file migration.</summary>
public enum MigrationState
{
    Planned = 0,
    Approved = 1,
    Copying = 2,
    Verifying = 3,
    Renaming = 4,
    Deleting = 5,
    Done = 6,
    Failed = 7,
    Cancelled = 8,
}

/// <summary>Hot/warm/cold storage class from the usage analyzer.</summary>
public enum ContentClass
{
    Cold = 0,
    Warm = 1,
    Hot = 2,
}

/// <summary>Purpose of an activation link.</summary>
public enum LinkKind
{
    Install = 0,
    Alias = 1,
    Temp = 2,
}

/// <summary>Filesystem link mechanism.</summary>
public enum LinkType
{
    Symlink = 0,
    Hardlink = 1,
}

/// <summary>Why an activation link exists (drives safe reference-counted deactivation).</summary>
public enum ActivationReason
{
    Explicit = 0,
    DependencyOf = 1,
    Temp = 2,
}

/// <summary>How a preset member's version is resolved.</summary>
public enum ResolutionMode
{
    Exact = 0,
    Latest = 1,
}

/// <summary>Scope of a persistent var alias.</summary>
public enum AliasScope
{
    Global = 0,
    Preset = 1,
}

/// <summary>Per-content-item favorite/hide state (round-trips VaM .fav/.hide).</summary>
public enum ContentItemPrefState
{
    Normal = 0,
    Fav = 1,
    Hide = 2,
}

/// <summary>Inferred gender of a content item.</summary>
public enum Gender
{
    Auto = 0,
    Female = 1,
    Male = 2,
    Futa = 3,
}

/// <summary>VaM content type of an entry inside a var.</summary>
public enum ContentType
{
    Unknown = 0,
    Scene = 1,
    Look = 2,
    Clothing = 3,
    Hairstyle = 4,
    Morph = 5,
    Pose = 6,
    Skin = 7,
    Plugin = 8,
    Asset = 9,
}

/// <summary>Durable per-VarFile ingest completeness for crash-resumable raw-first indexing. (A15.)</summary>
public enum IngestState
{
    Discovered = 0,
    Inspecting = 1,
    RawStored = 2,
    Failed = 3,
}

/// <summary>High-level scan run phase persisted on <see cref="ScanRun"/>.</summary>
public enum ScanPhase
{
    Discovering = 0,
    Ingesting = 1,
    Resolving = 2,
    Refreshing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}
