namespace VarVault.Sdk.Activation;

/// <summary>Result of building a preset's profile links.</summary>
public sealed record ActivationBuildResult(int LinksCreated, int MissingPackages);

/// <summary>
/// Builds a loading preset's profile link set: resolves members + their forward-dependency closure,
/// picks the hottest online copy of each package, and records an <c>ActivationLink</c> per package
/// (LinkKind=Install; Reason=Explicit for members, DependencyOf for pulled-in deps). Actual symlink
/// files are created by the symlink service where privilege allows. (Checklist 3.4/3.5/3.7.)
/// </summary>
public interface IActivationService
{
    Task<ActivationBuildResult> BuildProfileLinksAsync(long presetId, CancellationToken cancellationToken = default);
}
