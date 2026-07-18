namespace VarVault.Domain.Indexing;

/// <summary>
/// The fields read from a var's <c>meta.json</c>. Creator/Package here are the author's own names and
/// are stored only for divergence reporting — <b>never</b> used for identity (that comes from the
/// filename). Dependency refs are the keys of the <c>dependencies</c> object. (Data-arch §2, IDX-4.)
/// </summary>
public sealed record VarMeta(
    string? Creator,
    string? Package,
    string? LicenseType,
    string? Description,
    string? ProgramVersion,
    IReadOnlyList<string> DependencyRefs);
