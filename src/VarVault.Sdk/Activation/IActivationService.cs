namespace VarVault.Sdk.Activation;

/// <summary>
/// Result of building a preset's profile links.
/// <paramref name="LinksCreated"/> = links now present on disk for this build (install + alias);
/// <paramref name="LinksRemoved"/> = orphaned links deleted from disk;
/// <paramref name="MissingPackages"/> = closure packages with no online copy to link;
/// <paramref name="PrivilegeFailures"/> = symlink-privilege denials (Developer Mode). When &gt; 0 the build
/// aborted without partial links — surface the Developer-Mode hint. (Checklist 22 · T4.3/T4.4.)
/// </summary>
public sealed record ActivationBuildResult(
    int LinksCreated,
    int LinksRemoved,
    int MissingPackages,
    int PrivilegeFailures = 0);

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

    /// <summary>
    /// Reconcile Profile rows with the on-disk <c>___AddonPacksSwitch ___</c> directories: derive
    /// <c>IsActive</c> from the live AddonPackages symlink and prune Profile rows (and their links) whose
    /// directory has vanished. Called at startup. Returns the number of pruned profiles. (Checklist 22 · T3.3.)
    /// </summary>
    Task<int> ReconcileProfilesAsync(CancellationToken cancellationToken = default);
}
