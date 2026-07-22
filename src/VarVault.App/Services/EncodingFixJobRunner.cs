using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.App.Services;

/// <summary>
/// Enqueues encoding-fix work on <see cref="IJobQueue"/> (jobs panel + cancel + progress),
/// mirroring <see cref="ImportJobRunner"/>. Fresh DI scope per job; optional UI refresh on completion.
/// </summary>
public sealed class EncodingFixJobRunner(IJobQueue queue, IServiceScopeFactory scopes, IUiDispatcher? ui = null)
{
    /// <summary>Invoked on the UI thread after a job finishes (success, failure, or cancel). Wired by AppHost to refresh Health/Library.</summary>
    public Action? AfterCompleted { get; set; }

    /// <summary>Raise a shell toast from the UI thread when a job finishes successfully.</summary>
    public Action<string>? ShowToast { get; set; }

    public EncodingFixJob StartGroup(string? codepageFilter)
    {
        var label = string.IsNullOrWhiteSpace(codepageFilter)
            ? "Fix encoding (all groups)"
            : $"Fix encoding ({codepageFilter.Trim()})";
        return Enqueue(label, async (health, progress, ct) =>
            await health.FixGroupAsync(codepageFilter, progress, ct).ConfigureAwait(false));
    }

    public EncodingFixJob StartVarFiles(IReadOnlyList<long> varFileIds, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(varFileIds);
        var ids = varFileIds.Where(id => id > 0).Distinct().ToList();
        var label = title ?? (ids.Count == 1 ? "Fix encoding (1 file)" : $"Fix encoding ({ids.Count} selected)");
        return Enqueue(label, async (health, progress, ct) =>
            await health.FixManyAsync(ids, progress, ct).ConfigureAwait(false));
    }

    private EncodingFixJob Enqueue(string name, Func<IHealthService, IProgressSink, CancellationToken, Task<BulkActionResult>> work)
    {
        var tcs = new TaskCompletionSource<BulkActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = queue.Enqueue(name, async ctx =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var health = scope.ServiceProvider.GetRequiredService<IHealthService>();
                var result = await work(health, ctx.Progress, ctx.Cancellation).ConfigureAwait(false);
                tcs.TrySetResult(result);
                NotifyCompleted(result, cancelled: false);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
                NotifyCompleted(new BulkActionResult(0, 0), cancelled: true);
                throw;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                NotifyCompleted(new BulkActionResult(0, 0), cancelled: false, error: ex.Message);
                throw;
            }
        });
        return new EncodingFixJob(handle, tcs.Task);
    }

    private void NotifyCompleted(BulkActionResult result, bool cancelled, string? error = null)
    {
        void Raise()
        {
            AfterCompleted?.Invoke();
            if (cancelled)
                ShowToast?.Invoke("Encoding fix cancelled");
            else if (error is not null)
                ShowToast?.Invoke($"Encoding fix failed: {error}");
            else
                ShowToast?.Invoke(
                    $"Fixed {result.Succeeded} → UTF-8 · originals retained as .fixed.var lineage" +
                    (result.Failed > 0 ? $" · {result.Failed} skipped" : ""));
        }

        if (ui is not null)
            ui.Post(Raise);
        else
            Raise();
    }
}

public sealed record EncodingFixJob(JobHandle Handle, Task<BulkActionResult> Result);
