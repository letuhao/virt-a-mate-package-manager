namespace VarVault.Sdk.Library;

/// <summary>
/// Maintains the M:N <c>ProfilePackageLink</c> table and refreshes the active-profile columns on
/// <see cref="PackageListEntry"/> (<c>IsActive</c>/<c>InstalledAt</c>).
/// </summary>
public interface IProfilePackageLinkService
{
    /// <summary>Reconcile profile membership from current activation links (idempotent InstalledAt).</summary>
    Task SyncFromActivationLinksAsync(long profileId, CancellationToken cancellationToken = default);

    /// <summary>Stamp <c>PackageListItem.IsActive</c>/<c>InstalledAt</c> from the active profile's links.</summary>
    Task RefreshActiveProfileReadModelAsync(CancellationToken cancellationToken = default);
}
