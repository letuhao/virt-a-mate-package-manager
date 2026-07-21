using System.IO;
using System.Text;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Cross-process single-writer guard: an exclusively-held lock file beside the catalog DB. Whoever holds
/// it is the sole catalog writer. The indexer worker takes it on startup; the GUI must acquire it before
/// any in-process write fallback, so a live worker and the GUI can never write the DB concurrently. (A12.)
/// </summary>
public sealed class WriterLease : IDisposable
{
    private readonly FileStream _stream;

    private WriterLease(FileStream stream) => _stream = stream;

    /// <summary>The lock-file path for a given catalog DB path.</summary>
    public static string PathFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", "writer.lock");

    /// <summary>
    /// Try to become the sole writer. Returns null if another live process already holds the lease.
    /// Stamps the owning pid into the file for diagnostics.
    /// </summary>
    public static WriterLease? TryAcquire(string databasePath)
    {
        var path = PathFor(databasePath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // FileShare.None → a second acquirer fails until the holder releases (or its process dies,
            // which the OS uses to release the handle).
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            stream.SetLength(0);
            var stamp = Encoding.UTF8.GetBytes($"{Environment.ProcessId} {DateTime.UtcNow:O}");
            stream.Write(stamp, 0, stamp.Length);
            stream.Flush();
            return new WriterLease(stream);
        }
        catch (IOException)
        {
            return null; // held by another live process
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
