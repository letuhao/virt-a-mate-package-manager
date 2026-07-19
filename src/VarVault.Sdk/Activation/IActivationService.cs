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

    /// <summary>
    /// Deactivate an explicit member package: rebuild links from the remaining active members, so its
    /// pulled-in dependency links are dropped only when no other active member still needs them
    /// (reference-counted). Returns the resulting link set. (Checklist 3.15.)
    /// </summary>
    Task<ActivationBuildResult> DeactivateAsync(long presetId, long packageId, CancellationToken cancellationToken = default);

    /// <summary>Rescue baseline: remove all app-owned links from a profile (deactivate everything). (3.13)</summary>
    Task<int> RescueAsync(long profileId, CancellationToken cancellationToken = default);

    /// <summary>Clean up temp activation links after use. (3.14)</summary>
    Task<int> CleanTempLinksAsync(long profileId, CancellationToken cancellationToken = default);
}
