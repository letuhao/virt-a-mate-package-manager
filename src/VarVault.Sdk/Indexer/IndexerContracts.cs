using System.IO;
using System.Security.Cryptography;
using System.Text;
using VarVault.Common;

namespace VarVault.Sdk.Indexer;

/// <summary>Protocol version for the GUI ↔ Indexer named-pipe contract. (A12.)</summary>
public static class IndexerProtocol
{
    public const int Version = 1;
    public const string PipeNamePrefix = "VarVault.Indexer.";

    public static string PipeNameFor(string dataDirectoryHash) =>
        PipeNamePrefix + dataDirectoryHash;

    /// <summary>
    /// Stable pipe-name hash for a data directory. Trims trailing separators so
    /// <c>D:\VarVault</c> and <c>D:\VarVault\</c> (common in pointer files) resolve to the same pipe.
    /// </summary>
    public static string HashDataDirectory(string path)
    {
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }
}

public enum IndexerCommandKind
{
    Ping = 0,
    StartIndexAll = 1,
    StartIndexRepository = 2,
    CancelJob = 3,
    GetStatus = 4,
    Shutdown = 5,
    /// <summary>Register the caller as an owner so the worker won't self-exit while a GUI is attached.</summary>
    RegisterOwner = 6,
    /// <summary>Detach the caller as an owner (clean GUI shutdown); worker may self-exit once idle+orphaned.</summary>
    UnregisterOwner = 7,
}

public enum IndexerJobState
{
    Idle = 0,
    Discovering = 1,
    Ingesting = 2,
    Resolving = 3,
    Refreshing = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
}

public sealed record IndexerCommand(
    IndexerCommandKind Kind,
    Guid? JobId = null,
    Guid? RepositoryId = null,
    int ProtocolVersion = IndexerProtocol.Version,
    /// <summary>The caller process id, so the worker can watch owner liveness and self-exit when orphaned.</summary>
    int? OwnerProcessId = null,
    /// <summary>Bypass the unchanged-repository fast-path and re-scan every file. (A16 manual re-index.)</summary>
    bool ForceFull = false);

public sealed record IndexerStatus(
    int ProtocolVersion,
    IndexerJobState State,
    Guid? JobId,
    string? PhaseMessage,
    long Done,
    long Total,
    long WorkingSetBytes,
    int QueueDepth,
    int Errors,
    long CatalogGeneration,
    string? Error);

/// <summary>
/// Client surface the GUI uses to talk to the indexer worker. Implementations may be in-process
/// (tests) or named-pipe (production). (A12.)
/// </summary>
public interface IIndexerClient
{
    Task<Result<IndexerStatus>> PingAsync(CancellationToken cancellationToken = default);
    Task<Result<Guid>> StartIndexAllAsync(bool forceFull = false, CancellationToken cancellationToken = default);
    Task<Result<Guid>> StartIndexRepositoryAsync(Guid repositoryId, bool forceFull = false, CancellationToken cancellationToken = default);
    Task<Result> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<Result<IndexerStatus>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Attach this GUI as an owner so the worker stays alive while it's connected. (A12 liveness.)</summary>
    Task<Result> RegisterOwnerAsync(CancellationToken cancellationToken = default);

    /// <summary>Detach on clean shutdown so the worker may self-exit once idle. (A12 liveness.)</summary>
    Task<Result> UnregisterOwnerAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Server-side command handler hosted by <c>VarVault.Indexer</c>. Coalesces duplicate index
/// requests and owns the catalog write lease. (A12.)
/// </summary>
public interface IIndexerWorker
{
    Task<IndexerStatus> HandleAsync(IndexerCommand command, CancellationToken cancellationToken = default);
    IndexerStatus Snapshot { get; }

    /// <summary>True while an index job is running — the worker must not self-exit. (A12 liveness.)</summary>
    bool IsBusy { get; }

    /// <summary>True while at least one registered owner (GUI) process is still alive. (A12 liveness.)</summary>
    bool HasLiveOwner { get; }
}
