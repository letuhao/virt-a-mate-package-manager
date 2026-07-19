using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>
/// BE-N7 · Loading-profile switching facade over the VaM profile service. Reads the VaM root from
/// settings; switching repoints the single AddonPackages symlink (O(1)). (16-checklist BE-N7.)
/// </summary>
public interface IProfileService
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
    Task<string?> ActiveAsync(CancellationToken cancellationToken = default);
    Task<Result> CreateAsync(string profileName, CancellationToken cancellationToken = default);
    Task<Result> SwitchToAsync(string profileName, CancellationToken cancellationToken = default);
}
