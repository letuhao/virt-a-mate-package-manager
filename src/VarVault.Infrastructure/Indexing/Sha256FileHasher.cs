using System.IO;
using System.Security.Cryptography;
using VarVault.Common;
using VarVault.Domain.Dedup;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Streams a file through SHA-256 to produce the full <c>ContentHash</c>. Sequential-scan hint and a
/// modest buffer keep it HDD-friendly. (Checklist BE-F3.)
/// </summary>
public sealed class Sha256FileHasher : IFileHasher
{
    public async Task<Result<string>> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(path);
        try
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, FileOptions.SequentialScan | FileOptions.Asynchronous);

            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }
        catch (FileNotFoundException)
        {
            return Result.Failure<string>("hash.missing", $"File not found: {path}");
        }
        catch (IOException ex)
        {
            return Result.Failure<string>("hash.io", ex.Message);
        }
    }
}
