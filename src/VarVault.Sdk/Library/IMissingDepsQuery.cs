namespace VarVault.Sdk.Library;

/// <summary>A missing dependency ref, how many packages need it, and the owned var a global alias maps it to (if any). (doc 26 · F-9)</summary>
public sealed record MissingDependency(string Ref, int NeededByCount, string? AliasTarget = null);

/// <summary>Lists unresolved dependency refs with their needed-by counts, for the missing-deps screen. (2.14.)</summary>
public interface IMissingDepsQuery
{
    Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default);
}
