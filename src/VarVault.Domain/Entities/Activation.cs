namespace VarVault.Domain.Entities;

/// <summary>
/// A physical AddonPackages profile directory. Switching = repoint the one AddonPackages
/// directory symlink → instant, O(1) regardless of var count. (Data-arch §3, §5.7.)
/// </summary>
public sealed class Profile
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DirPath { get; set; } = string.Empty;

    /// <summary>The one profile the AddonPackages symlink currently points at.</summary>
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ActivationLink> Links { get; } = new List<ActivationLink>();
}

/// <summary>A file link the app created inside a profile, pointing at a specific (hottest online) var copy.</summary>
public sealed class ActivationLink
{
    public long Id { get; set; }

    public long ProfileId { get; set; }
    public Profile? Profile { get; set; }

    public long VarFileId { get; set; }
    public VarFile? VarFile { get; set; }

    public string LinkPath { get; set; } = string.Empty;
    public LinkKind LinkKind { get; set; }

    /// <summary>For a renamed alias link that names a still-missing ref.</summary>
    public string? AliasedMissingRefKey { get; set; }
    public string? LinkSubfolder { get; set; }
    public LinkType LinkType { get; set; }
    public ActivationReason Reason { get; set; }

    /// <summary>Attribution so safe deactivation can reference-count auto-pulled dependency links.</summary>
    public long? RequestedByPresetId { get; set; }
    public LoadingPreset? RequestedByPreset { get; set; }
}

/// <summary>A named var-set realized as a <see cref="Profile"/>. (Data-arch §3.)</summary>
public sealed class LoadingPreset
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public long? ProfileId { get; set; }
    public Profile? Profile { get; set; }

    public bool IsRuleBased { get; set; }
    public string? RuleJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<PresetMember> Members { get; } = new List<PresetMember>();
}

/// <summary>A package reference in a preset, by folded name (portable), with a snapshot resolution.</summary>
public sealed class PresetMember
{
    public long Id { get; set; }

    public long PresetId { get; set; }
    public LoadingPreset? Preset { get; set; }

    public string PackageRefKey { get; set; } = string.Empty;
    public string PackageRefRaw { get; set; } = string.Empty;
    public ResolutionMode ResolutionMode { get; set; }

    public long? ResolvedPackageId { get; set; }
    public Package? ResolvedPackage { get; set; }

    public string? ResolvedVersion { get; set; }
    public bool IsVersionSubstituted { get; set; }
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Persistent missing-var resolution, serialized by var-name string (portable across machines).</summary>
public sealed class VarAlias
{
    public long Id { get; set; }

    public string MissingRefKey { get; set; } = string.Empty;
    public string MissingRefRaw { get; set; } = string.Empty;

    /// <summary>Portable target var name; re-resolved on import with a reported diff.</summary>
    public string? ResolvedVarName { get; set; }
    public long? ResolvedPackageId { get; set; }
    public Package? ResolvedPackage { get; set; }

    public AliasScope Scope { get; set; }
    public long? PresetId { get; set; }
    public LoadingPreset? Preset { get; set; }
    public DateTime CreatedAt { get; set; }
}
