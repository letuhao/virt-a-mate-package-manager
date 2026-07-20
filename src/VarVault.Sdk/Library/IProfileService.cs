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

    /// <summary>Delete a profile (refuses the active one). (doc 26 · G-6)</summary>
    Task<Result> DeleteAsync(string profileName, CancellationToken cancellationToken = default);

    /// <summary>Rename a profile (repoints the active symlink if needed). (doc 26 · G-6)</summary>
    Task<Result> RenameAsync(string oldName, string newName, CancellationToken cancellationToken = default);
}
