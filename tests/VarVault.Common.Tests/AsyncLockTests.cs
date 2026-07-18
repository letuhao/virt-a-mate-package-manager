using VarVault.Common.Threading;

namespace VarVault.Common.Tests;

public class AsyncLockTests
{
    [Fact]
    public async Task Serializes_concurrent_sections()
    {
        var asyncLock = new AsyncLock();
        var inside = 0;
        var maxInside = 0;

        async Task Worker()
        {
            using (await asyncLock.AcquireAsync())
            {
                var current = Interlocked.Increment(ref inside);
                maxInside = Math.Max(maxInside, current);
                await Task.Delay(10);
                Interlocked.Decrement(ref inside);
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Worker()));

        Assert.Equal(1, maxInside); // never more than one holder at a time
    }
}
