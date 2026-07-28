using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Indexer;

namespace VarVault.App.Composition;

/// <summary>
/// Keeps a live indexer client available for the whole session: pings on an interval and, if the worker
/// has died, re-resolves (respawn / reconnect / in-proc fallback) and swaps <see cref="IndexerClientOverride.Current"/>.
/// Startup only resolves once; this closes the "worker dies mid-session" gap. (A12 liveness.)
/// </summary>
public sealed class IndexerHealthMonitor(IServiceProvider services, string dataDirectory) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, ct).ConfigureAwait(false);
                var client = IndexerClientOverride.Current;
                var alive = client is not null && await IsAliveAsync(client, ct).ConfigureAwait(false);
                if (!alive)
                {
                    // Re-resolve: reconnect to a running worker, respawn one, or fall back to in-proc.
                    var resolved = IndexerProcessHost.ResolveClient(services, dataDirectory);
                    IndexerClientOverride.Current = resolved;
                    try
                    {
                        services.GetRequiredService<Infrastructure.Indexing.IndexerClientHub>()
                            .Redirect(resolved);
                    }
                    catch
                    {
                        // Hub may be absent in minimal hosts.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // best-effort; try again next tick
            }
        }
    }

    private static async Task<bool> IsAliveAsync(IIndexerClient client, CancellationToken ct)
    {
        try
        {
            // Prefer the short liveness probe when talking to a pipe client — a dead worker must not
            // burn the full 5s command timeout every health tick.
            if (client is Infrastructure.Indexing.NamedPipeIndexerClient pipe)
            {
                var live = await pipe.PingLivenessAsync(ct).ConfigureAwait(false);
                return live.IsSuccess;
            }
            var ping = await client.PingAsync(ct).ConfigureAwait(false);
            return ping.IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
