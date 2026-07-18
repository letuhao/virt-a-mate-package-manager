namespace VarVault.Domain.Entities;

/// <summary>A user tag. <see cref="NameKey"/> is folded and unique. (Data-arch §3 Discovery.)</summary>
public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;

    public ICollection<PackageTag> PackageTags { get; } = new List<PackageTag>();
}

/// <summary>Join: a tag applied to a package.</summary>
public sealed class PackageTag
{
    public long PackageId { get; set; }
    public Package? Package { get; set; }
    public long TagId { get; set; }
    public Tag? Tag { get; set; }
}

/// <summary>A manual or rule-based collection of packages.</summary>
public sealed class Collection
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsRuleBased { get; set; }
    public string? RuleJson { get; set; }

    public ICollection<CollectionMember> Members { get; } = new List<CollectionMember>();
}

/// <summary>Join: a package in a collection.</summary>
public sealed class CollectionMember
{
    public long CollectionId { get; set; }
    public Collection? Collection { get; set; }
    public long PackageId { get; set; }
    public Package? Package { get; set; }
}

/// <summary>Per-content-item favorite/hide preference (round-trips VaM's .fav/.hide sidecars).</summary>
public sealed class ContentItemPref
{
    public long Id { get; set; }
    public long ContentItemId { get; set; }
    public ContentItem? ContentItem { get; set; }
    public ContentItemPrefState State { get; set; } = ContentItemPrefState.Normal;
}
