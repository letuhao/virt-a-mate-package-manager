using VarVault.Common;

namespace VarVault.Domain.Dedup;

/// <summary>
/// Computes a file's full content hash — the <b>verified</b> equality check that authorizes deletion.
/// Lazy: computed only for verify-before-delete and portability re-match, never during normal indexing.
/// Implemented by Infrastructure (streaming I/O). (Checklist BE-F3, 4.1.)
/// </summary>
public interface IFileHasher
{
    Task<Result<string>> ComputeAsync(string path, CancellationToken cancellationToken = default);
}
