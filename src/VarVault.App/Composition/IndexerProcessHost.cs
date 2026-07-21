using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Infrastructure.Indexing;
using VarVault.Sdk.Indexer;

namespace VarVault.App.Composition;

/// <summary>
/// Ensures a <c>VarVault.Indexer</c> worker is reachable. Prefers named-pipe to an existing/spawned
/// process; falls back to the in-process <see cref="IIndexerClient"/> already registered by persistence.
/// The in-process fallback only runs if this GUI can take the single-writer lease, so a live worker and
/// the GUI can never write the catalog concurrently. (A12 liveness.)
/// </summary>
public static class IndexerProcessHost
{
    /// <summary>How long to wait for a just-spawned worker's pipe before falling back in-process.</summary>
    private static readonly TimeSpan SpawnWait = TimeSpan.FromSeconds(15);

    public static IIndexerClient ResolveClient(IServiceProvider services, string dataDirectory)
    {
        var pipeName = IndexerProtocol.PipeNameFor(IndexerProtocol.HashDataDirectory(dataDirectory));
        var pipeClient = new NamedPipeIndexerClient(pipeName, Environment.ProcessId);

        // 1. Already-running worker? (short probe — never block the UI for seconds here.)
        if (PipeAlive(pipeClient))
            return Attach(pipeClient);

        // 2. Spawn one and wait for its pipe with short polls (total capped by SpawnWait).
        TrySpawn(dataDirectory);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < SpawnWait)
        {
            if (PipeAlive(pipeClient))
                return Attach(pipeClient);
            Thread.Sleep(100);
        }

        // 3. No worker → in-process, but only if we can be the sole writer.
        var dbPath = Path.Combine(dataDirectory, "catalog.db");
        var lease = WriterLease.TryAcquire(dbPath);
        if (lease is not null)
        {
            IndexerClientOverride.Lease = lease;
            return services.GetRequiredService<IIndexerClient>();
        }

        // A live writer holds the lease but its pipe isn't up yet — keep the pipe client; callers
        // degrade until it answers. Do NOT fall into a second writer.
        return pipeClient;
    }

    private static IIndexerClient Attach(NamedPipeIndexerClient client)
    {
        try { _ = client.RegisterOwnerAsync().GetAwaiter().GetResult(); }
        catch { /* best-effort */ }
        return client;
    }

    private static bool PipeAlive(NamedPipeIndexerClient client)
    {
        try
        {
            return client.PingLivenessAsync().GetAwaiter().GetResult().IsSuccess;
        }
        catch
        {
            return false;
        }
    }

    private static void TrySpawn(string dataDirectory)
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "VarVault.Indexer.exe");
            var isDll = false;
            if (!File.Exists(exe))
            {
                exe = Path.Combine(AppContext.BaseDirectory, "VarVault.Indexer.dll");
                isDll = true;
            }
            if (!File.Exists(exe))
                return;

            // Normalize so the worker hashes the same pipe name as this process (trailing slash).
            var normalized = Path.GetFullPath(dataDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var ownerArgs = $"--owner {Environment.ProcessId}";
            var start = new ProcessStartInfo
            {
                FileName = isDll ? "dotnet" : exe,
                Arguments = isDll
                    ? $"\"{exe}\" \"{normalized}\" {ownerArgs}"
                    : $"\"{normalized}\" {ownerArgs}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            Process.Start(start);
        }
        catch
        {
            // best-effort; in-process fallback remains
        }
    }
}
