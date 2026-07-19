namespace VarVault.Sdk.Library;

/// <summary>A request to move one var copy to a target repository.</summary>
public sealed record MigrationRequest(long VarFileId, System.Guid TargetRepositoryId);

/// <summary>Outcome of running a set of moves.</summary>
public sealed record MigrationRunResult(int Moved, int Failed);

/// <summary>
/// BE-N3 · Runs approved tier moves through the durable state machine (copy → verify → rename → trash
/// source), re-pointing references before deleting. Never moves to a removable/network tier. Run as an
/// <c>IJobQueue</c> job by the shell. (16-checklist BE-N3.)
/// </summary>
public interface IMigrationService
{
    Task<MigrationRunResult> RunAsync(IReadOnlyList<MigrationRequest> moves, CancellationToken cancellationToken = default);
}
