using System.IO;
using VarVault.Sdk.Threading;

namespace VarVault.Infrastructure.Threading;

/// <summary>No-op lock: single-process hosts (tests, CLI) need no cross-process coordination.</summary>
public sealed class NullWriteLock : IGlobalWriteLock
{
    private static readonly IAsyncDisposable Scope = new NoopScope();
    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default) => Task.FromResult(Scope);

    private sealed class NoopScope : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// File-based cross-process write mutex: an exclusively-opened lock file next to the catalog DB. Whoever
/// holds the handle is the sole writer; the OS releases it if the holder crashes, so it self-heals (unlike
/// a named semaphore). Acquire retries with short backoff until the other process releases. (A12.)
/// </summary>
public sealed class FileGlobalWriteLock(string lockFilePath) : IGlobalWriteLock
{
    public static string PathFor(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".", "catalog.write.lock");

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockFilePath))!);
        var delayMs = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.None);
                return new Scope(stream);
            }
            catch (IOException)
            {
                // Held by another process — back off (capped) and retry.
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                delayMs = Math.Min(delayMs * 2, 25);
            }
        }
    }

    private sealed class Scope(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            stream.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
