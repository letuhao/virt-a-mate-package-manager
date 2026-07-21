using Microsoft.Extensions.DependencyInjection;
using VarVault.Sdk.Import;
using VarVault.Sdk.Threading;

namespace VarVault.App.Services;

/// <summary>
/// Enqueues import Scan/Apply work on <see cref="IJobQueue"/> with a fresh DI scope per job.
/// Results are surfaced via <see cref="ImportJob{T}.Result"/>; worker threads never touch UI observables.
/// </summary>
public sealed class ImportJobRunner(IJobQueue queue, IServiceScopeFactory scopes)
{
    public ImportJob<ImportSession> StartScan(ImportSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var tcs = new TaskCompletionSource<ImportSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = queue.Enqueue("Scanning import sources", async ctx =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var import = scope.ServiceProvider.GetRequiredService<IImportService>();
                var session = await import.ScanAsync(spec, ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
                tcs.TrySetResult(session);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }
        });
        return new ImportJob<ImportSession>(handle, tcs.Task);
    }

    public ImportJob<ApplyResult> StartApply(ImportSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var tcs = new TaskCompletionSource<ApplyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = queue.Enqueue("Applying import", async ctx =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var import = scope.ServiceProvider.GetRequiredService<IImportService>();
                var result = await import.ApplyAsync(session, ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
                tcs.TrySetResult(result);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }
        });
        return new ImportJob<ApplyResult>(handle, tcs.Task);
    }

    /// <summary>Freeze the user's current decisions into an immutable apply snapshot.</summary>
    public static ImportSession FreezeSession(ImportSession session, IEnumerable<(Guid Id, ImportDecision Decision)> decisions)
    {
        var map = decisions.ToDictionary(d => d.Id, d => d.Decision);
        var frozen = session.Items.Select(item =>
        {
            // Copy first so the live review list can keep mutating its own ImportItem instances.
            var copy = item with { };
            copy.Decision = map.TryGetValue(item.Id, out var dec) ? dec : item.Decision;
            return copy;
        }).ToList();
        return session with { Items = frozen };
    }
}

public sealed record ImportJob<T>(JobHandle Handle, Task<T> Result);
