namespace VarVault.Sdk.Library;

/// <summary>A missing dependency ref and how many packages need it.</summary>
public sealed record MissingDependency(string Ref, int NeededByCount);

/// <summary>Lists unresolved dependency refs with their needed-by counts, for the missing-deps screen. (2.14.)</summary>
public interface IMissingDepsQuery
{
    Task<IReadOnlyList<MissingDependency>> GetMissingAsync(CancellationToken cancellationToken = default);
}
