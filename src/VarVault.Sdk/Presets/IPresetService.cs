using VarVault.Common;
using VarVault.Sdk.Paging;

namespace VarVault.Sdk.Presets;

/// <summary>A loading preset (a named var-set).</summary>
public sealed record PresetInfo(long Id, string Name, int MemberCount);

/// <summary>
/// The dependency-aware activation preview: how many packages a preset resolves to directly, how many
/// including the pulled-in forward closure, and which member refs are missing. (Checklist 3.8.)
/// </summary>
public sealed record ActivationPreview(int DirectResolved, int TotalWithClosure, IReadOnlyList<string> MissingRefs);

/// <summary>
/// Loading-preset CRUD and activation planning. Members are stored by name (portable). (Checklist 3.6/3.8.)
/// </summary>
public interface IPresetService
{
    Task<Result<PresetInfo>> CreateAsync(string name, IEnumerable<string> memberRefs, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PresetInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long presetId, CancellationToken cancellationToken = default);
    Task<Result<PresetInfo>> AddMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default);

    /// <summary>Remove a member ref from a preset (edit-preset dialog). Returns the updated info. (BE-G3)</summary>
    Task<Result<PresetInfo>> RemoveMemberAsync(long presetId, string memberRef, CancellationToken cancellationToken = default);

    /// <summary>The member refs of a preset, in order (edit-preset dialog member table). (BE-G3)</summary>
    async Task<PageResult<string>> MembersPageAsync(long presetId, PageRequest request, CancellationToken cancellationToken = default)
    {
        var all = await MembersAsync(presetId, cancellationToken).ConfigureAwait(false);
        var page = request.Normalize();
        return new PageResult<string>(all.Skip(page.Skip).Take(page.SafePageSize).ToList(), all.Count, page.SafePageNumber, page.SafePageSize);
    }
    Task<IReadOnlyList<string>> MembersAsync(long presetId, CancellationToken cancellationToken = default);

    /// <summary>Resolve members + pull the forward-dependency closure → "will pull in N" preview. (3.8)</summary>
    Task<ActivationPreview?> PreviewActivationAsync(long presetId, CancellationToken cancellationToken = default);
}
