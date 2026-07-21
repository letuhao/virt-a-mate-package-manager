using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Host;
using VarVault.Infrastructure.Indexing;
using VarVault.Sdk.Indexer;

namespace VarVault.Indexer;

/// <summary>
/// Headless indexer worker process: owns catalog writes, durable scan ledger, and named-pipe control.
/// GUI connects via <see cref="NamedPipeIndexerClient"/>. Self-exits when idle and orphaned. (A12.)
/// </summary>
public static class Program
{
    /// <summary>How long the worker stays up after the last owner (GUI) leaves and it goes idle.</summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromSeconds(30);

    public static async Task<int> Main(string[] args)
    {
        var (dataDir, ownerPid) = ParseArgs(args);
        Directory.CreateDirectory(dataDir);

        // Single-writer guard: if another live writer already holds the lease, don't start a second one.
        var dbPath = Path.Combine(dataDir, "catalog.db");
        using var lease = WriterLease.TryAcquire(dbPath);
        if (lease is null)
        {
            Console.Error.WriteLine("VarVault.Indexer: another writer holds the catalog lease; exiting.");
            return 2;
        }

        await using var host = Bootstrap.BuildApp(dataDir);
        var worker = host.Services.GetRequiredService<IIndexerWorker>();
        // Seed the launching GUI as an owner immediately — otherwise the orphan watchdog treats
        // `--owner <pid>` as "saw an owner" while HasLiveOwner is still false and self-exits in 30s
        // before the first pipe command arrives.
        if (ownerPid > 0)
        {
            _ = await worker.HandleAsync(
                new IndexerCommand(IndexerCommandKind.RegisterOwner, OwnerProcessId: ownerPid),
                CancellationToken.None).ConfigureAwait(false);
        }
        var pipeName = IndexerProtocol.PipeNameFor(IndexerProtocol.HashDataDirectory(dataDir));
        Console.WriteLine($"VarVault.Indexer listening on {pipeName}");
        Console.WriteLine($"Data directory: {dataDir}");
        if (ownerPid > 0)
            Console.WriteLine($"Owner pid: {ownerPid}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // If launched with a known owner that has already died before we could serve it, still honor the grace
        // window via the watchdog (the owner registers itself on its first command).
        var watchdog = RunWatchdogAsync(worker, ownerPid, cts);

        var server = new NamedPipeIndexerServer(pipeName, worker);
        await server.RunAsync(cts.Token).ConfigureAwait(false);
        await watchdog.ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Shuts the worker down once it is idle AND no owner GUI is alive for <see cref="OrphanGrace"/>.
    /// A running job or any live owner resets the timer, so indexing survives GUI closure. (A12 liveness.)
    /// </summary>
    private static async Task RunWatchdogAsync(IIndexerWorker worker, int initialOwnerPid, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        DateTime? orphanedSince = null;
        var sawAnyOwner = initialOwnerPid > 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                var hasOwner = worker.HasLiveOwner;
                if (hasOwner)
                    sawAnyOwner = true;

                // Don't self-exit until a GUI has connected at least once, or an owner pid was supplied.
                var idleAndOrphaned = !worker.IsBusy && !hasOwner && sawAnyOwner;
                if (!idleAndOrphaned)
                {
                    orphanedSince = null;
                    continue;
                }

                orphanedSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - orphanedSince >= OrphanGrace)
                {
                    Console.WriteLine("VarVault.Indexer: idle and orphaned; shutting down.");
                    cts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private static (string DataDir, int OwnerPid) ParseArgs(string[] args)
    {
        string? dataDir = null;
        var ownerPid = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--owner", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _ = int.TryParse(args[++i], out ownerPid);
            }
            else if (dataDir is null)
            {
                dataDir = a;
            }
        }
        return (dataDir ?? ResolveDataDir(), ownerPid);
    }

    private static string ResolveDataDir()
    {
        var env = Environment.GetEnvironmentVariable("VARVAULT_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarVault");
    }
}
