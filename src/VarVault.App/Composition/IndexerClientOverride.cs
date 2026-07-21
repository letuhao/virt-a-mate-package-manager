using VarVault.Infrastructure.Indexing;
using VarVault.Sdk.Indexer;

namespace VarVault.App.Composition;

/// <summary>
/// Process-wide indexer client chosen at startup (pipe or in-proc), plus the writer lease held when this
/// GUI is acting as the in-process writer. Disposed on clean shutdown. (A12 liveness.)
/// </summary>
internal static class IndexerClientOverride
{
    public static IIndexerClient? Current { get; set; }

    /// <summary>Held only when this GUI is the in-process catalog writer (no out-of-process worker).</summary>
    public static WriterLease? Lease { get; set; }

    /// <summary>Background liveness monitor that respawns/reconnects the worker mid-session.</summary>
    public static IndexerHealthMonitor? Monitor { get; set; }

    /// <summary>Clean detach on GUI exit: stop the monitor, tell the worker we're gone, release the writer lease.</summary>
    public static async Task ShutdownAsync()
    {
        Monitor?.Dispose();
        Monitor = null;
        var client = Current;
        if (client is not null)
        {
            try { await client.UnregisterOwnerAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }
        Lease?.Dispose();
        Lease = null;
    }
}
