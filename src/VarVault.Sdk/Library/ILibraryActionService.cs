using VarVault.Common;

namespace VarVault.Sdk.Library;

/// <summary>Outcome of a bulk library action.</summary>
public sealed record BulkActionResult(int Succeeded, int Failed);

/// <summary>Outcome of resolving a txt package list against the owned library. (BE-G1)</summary>
public sealed record TxtResolveResult(IReadOnlyList<long> MatchedPackageIds, IReadOnlyList<string> Unmatched);

/// <summary>
/// BE-N10 · Bulk actions from the library ops-bar. The safe, composable ops (add-to-preset, fix-encoding,
/// delete-to-trash, export-txt) compose existing facades; each delete is gated by the deletion predicate.
/// (Install/uninstall/move are wired at the ops-bar screen via activation/migration.) (16-checklist BE-N10.)
/// </summary>
public interface ILibraryActionService
{
    Task<BulkActionResult> AddToPresetAsync(long presetId, IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default);
    Task<BulkActionResult> FixEncodingAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default);
    Task<BulkActionResult> DeleteAsync(IReadOnlyList<long> varFileIds, CancellationToken cancellationToken = default);
    Task<string> ExportTxtAsync(IReadOnlyList<long> packageIds, CancellationToken cancellationToken = default);

    /// <summary>Move each var file into a sub-folder within its own repository (durable, same-volume). (BE-G1)</summary>
    Task<BulkActionResult> MoveToSubfolderAsync(IReadOnlyList<long> varFileIds, string subfolder, CancellationToken cancellationToken = default);

    /// <summary>Resolve a txt list of package names to owned package ids (install-from-txt intake). (BE-G1)</summary>
    Task<TxtResolveResult> ResolveTxtAsync(string txt, CancellationToken cancellationToken = default);
}
