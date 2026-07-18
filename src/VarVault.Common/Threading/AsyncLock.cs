namespace VarVault.Common.Threading;

/// <summary>
/// Async-friendly mutual exclusion (a <see cref="SemaphoreSlim"/> of count 1) usable with
/// <c>await using</c>. Prefer this over <c>lock</c> when the critical section awaits.
/// </summary>
public sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async ValueTask<Releaser> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
