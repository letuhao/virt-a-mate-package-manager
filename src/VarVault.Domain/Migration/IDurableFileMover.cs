using VarVault.Common;

namespace VarVault.Domain.Migration;

/// <summary>Result of a verified copy: the destination and the hash both copies share.</summary>
public sealed record DurableCopyOutcome(string DestinationPath, string VerifiedHash);

/// <summary>
/// 🔒⚠ Moves a file durably: copy to a <c>.partial</c> temp → flush to disk → verify by re-reading and
/// comparing the full hash → atomic rename into place. The <b>source is left intact</b> — the caller
/// re-points every reference and inserts the target row only after this succeeds, then deletes the
/// source last (never before). Implemented by Infrastructure. (Data-arch §5.6; checklist 5.8/5.13.)
/// </summary>
public interface IDurableFileMover
{
    /// <summary>
    /// Copy <paramref name="sourcePath"/> to <paramref name="destinationPath"/> durably and verified.
    /// Returns a failure (leaving only a <c>.partial</c>, never a half-written destination) if the copy
    /// can't be verified. Refuses if the destination already exists.
    /// </summary>
    Task<Result<DurableCopyOutcome>> CopyVerifyRenameAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
