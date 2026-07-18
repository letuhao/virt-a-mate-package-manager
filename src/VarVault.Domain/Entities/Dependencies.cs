namespace VarVault.Domain.Entities;

/// <summary>
/// A dependency edge harvested per <see cref="VarFile"/> (from meta.json AND embedded scene/.vap refs),
/// resolved per Package. <c>UNIQUE(VarFileId, DependsOnRefKey)</c>. (Data-arch §3, §5.3.)
/// </summary>
public sealed class Dependency
{
    public long Id { get; set; }

    public long VarFileId { get; set; }
    public VarFile? VarFile { get; set; }

    /// <summary>Folded reference key (matches <see cref="Package.IdentityKey"/>).</summary>
    public string DependsOnRefKey { get; set; } = string.Empty;
    public string DependsOnRefRaw { get; set; } = string.Empty;
    public RefKind RefKind { get; set; }

    public long? ResolvedPackageId { get; set; }
    public Package? ResolvedPackage { get; set; }

    public bool IsVersionSubstituted { get; set; }
    public bool IsMissing { get; set; }
    public ResolvedVia ResolvedVia { get; set; } = ResolvedVia.None;
}

/// <summary>A user's own loose scene/look/preset under <c>{vampath}\Saves</c> or <c>Custom\…</c>. (The old savedepens.)</summary>
public sealed class UserSave
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string? Type { get; set; }
    public DateTime Mtime { get; set; }
    public DateTime LastScannedAt { get; set; }

    public ICollection<SaveDependency> Dependencies { get; } = new List<SaveDependency>();
}

/// <summary>A dependency of a <see cref="UserSave"/> — folded into reverse-closure so user-needed vars aren't "orphans".</summary>
public sealed class SaveDependency
{
    public long Id { get; set; }

    public long UserSaveId { get; set; }
    public UserSave? UserSave { get; set; }

    public string DependsOnRefKey { get; set; } = string.Empty;
    public string DependsOnRefRaw { get; set; } = string.Empty;

    public long? ResolvedPackageId { get; set; }
    public Package? ResolvedPackage { get; set; }

    public bool IsMissing { get; set; }
}
