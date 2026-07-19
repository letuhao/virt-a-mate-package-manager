namespace VarVault.Sdk.Library;

/// <summary>The kind of analyzer proposal (drives which runner Approve dispatches to).</summary>
public enum ProposalKind { Rebalance, Dedup, EncodingFix, RetireStale }

/// <summary>A propose-only action awaiting user approval. Nothing runs until approved.</summary>
public sealed record Proposal(
    string Id,
    ProposalKind Kind,
    string Title,
    string Detail,
    long AffectedBytes,
    IReadOnlyList<long> VarFileIds,
    string? Payload = null);

/// <summary>Outcome of approving/rejecting a proposal.</summary>
public sealed record ProposalActionResult(bool Ok, string Message);

/// <summary>
/// BE-N8 · Aggregates propose-only actions across tiering (rebalance), dedup, encoding fixes, and stale
/// retirement. Approve dispatches to the matching runner (migration/reclaim/health/trash); nothing runs
/// unattended. (16-checklist BE-N8.)
/// </summary>
public interface IProposalService
{
    Task<IReadOnlyList<Proposal>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProposalActionResult> ApproveAsync(Proposal proposal, CancellationToken cancellationToken = default);
    Task<ProposalActionResult> RejectAsync(Proposal proposal, CancellationToken cancellationToken = default);
}
