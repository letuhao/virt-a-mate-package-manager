namespace VarVault.Sdk.Library;

/// <summary>One physical copy in a duplicate group.</summary>
public sealed record DuplicateCopy(long VarFileId, int Tier, string Path, bool IsOnline, long SizeBytes);

/// <summary>A set of content-identical copies within one identity (reclaim candidates).</summary>
public sealed record DuplicateGroup(string IdentityKey, string ContentSignature, IReadOnlyList<DuplicateCopy> Copies);

/// <summary>
/// A set of vars that share the same content <b>payload</b> (content minus meta.json) across <b>different</b>
/// identities — a near-duplicate: the same content re-uploaded under another name/version, or a meta rewrite.
/// Read-only (reclaim stays exact-only). <see cref="Names"/> lists the distinct identity keys. (24-checklist A9.)
/// </summary>
public sealed record NearDuplicateGroup(string PayloadSignature, string Names, IReadOnlyList<DuplicateCopy> Members);

/// <summary>Outcome of a reclaim (delete-to-trash) operation.</summary>
public sealed record ReclaimResult(int Trashed, int Blocked);

/// <summary>
/// BE-N4 · Duplicate reclaim over DedupGrouping + DeletionPredicate. Lists exact within-identity duplicate
/// groups and trashes redundant copies — but only ones a verified online duplicate protects (single-copy is
/// never deletable; heuristics never authorize). (16-checklist BE-N4.)
/// </summary>
public interface IReclaimService
{
    Task<IReadOnlyList<DuplicateGroup>> ExactGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Near-duplicate groups: same payload signature across ≥2 distinct identities. Read-only. (24-checklist A9.)</summary>
    Task<IReadOnlyList<NearDuplicateGroup>> NearDuplicateGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Keep one copy; trash the listed redundant copies (each gated by the deletion predicate).</summary>
    Task<ReclaimResult> TrashRedundantAsync(long keepVarFileId, IReadOnlyList<long> trashVarFileIds, CancellationToken cancellationToken = default);
}
