namespace VarVault.Sdk.Import;

/// <summary>Why Quick import from the active VaM profile is ready or blocked.</summary>
public enum LooseVarsLocateStatus
{
    /// <summary>Profile folder exists; <see cref="LooseVarsLocateResult.LooseVarCount"/> may be zero.</summary>
    Ready = 0,
    NoVamPath = 1,
    NoActiveProfile = 2,
    ProfileMissing = 3,
}

/// <summary>Active-profile folder used for in-game download tidy (Quick import).</summary>
public sealed record LooseVarsLocateResult(
    LooseVarsLocateStatus Status,
    string? ProfilePath,
    string? ProfileName,
    int LooseVarCount,
    string Hint);

/// <summary>
/// Resolves the active VaM profile directory (where AddonPackages points) and counts real loose
/// <c>.var</c> files next to symlink farms — legacy TidyVars intake source.
/// </summary>
public interface IAddonPackagesLooseVarsLocator
{
    Task<LooseVarsLocateResult> LocateAsync(CancellationToken cancellationToken = default);
}
