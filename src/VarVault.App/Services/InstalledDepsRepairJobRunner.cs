using Microsoft.Extensions.DependencyInjection;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.Sdk.Threading;

namespace VarVault.App.Services;

/// <summary>
/// One-shot installed-deps repair on <see cref="IJobQueue"/>: analyse active packages' deps, activate
/// found (exact/latest/closest), return leftovers for alias Resolve. (Legacy MissingDepends.)
/// </summary>
public sealed class InstalledDepsRepairJobRunner(IJobQueue queue, IServiceScopeFactory scopes, IUiDispatcher? ui = null)
{
    public Action? AfterCompleted { get; set; }
    public Action<string>? ShowToast { get; set; }

    public InstalledDepsRepairJob Start()
    {
        var tcs = new TaskCompletionSource<InstalledDepsRepairOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = queue.Enqueue("Analyze installed dependencies", async ctx =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repair = scope.ServiceProvider.GetRequiredService<IInstalledDepsRepair>();
                ctx.Progress.Report(new ProgressReport(0, 2, "Analyzing dependencies of installed packages…"));
                var analysis = await repair.AnalyzeAsync(ctx.Cancellation).ConfigureAwait(false);

                ctx.Progress.Report(new ProgressReport(1, 2, "Activating found packages…"));
                var activation = await repair.ActivateFromAnalysisAsync(analysis, ctx.Cancellation).ConfigureAwait(false);

                ctx.Progress.Report(new ProgressReport(2, 2, "Done"));
                var outcome = new InstalledDepsRepairOutcome(analysis, activation);
                tcs.TrySetResult(outcome);
                NotifyCompleted(outcome, cancelled: false);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
                NotifyCompleted(null, cancelled: true);
                throw;
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                NotifyCompleted(null, cancelled: false, error: ex.Message);
                throw;
            }
        });
        return new InstalledDepsRepairJob(handle, tcs.Task);
    }

    private void NotifyCompleted(InstalledDepsRepairOutcome? outcome, bool cancelled, string? error = null)
    {
        void Raise()
        {
            AfterCompleted?.Invoke();
            if (cancelled)
                ShowToast?.Invoke("Installed-deps analyze cancelled");
            else if (error is not null)
                ShowToast?.Invoke($"Installed-deps analyze failed: {error}");
            else if (outcome is not null)
            {
                var a = outcome.Analysis;
                var act = outcome.Activation;
                ShowToast?.Invoke(
                    $"Installed deps: {a.ActivePackageCount} active · {a.InLibrary} found · {a.NotInLibrary} missing" +
                    (act.MembersActivated > 0 ? $" · activated {act.MembersActivated}" : "") +
                    (a.Leftovers.Count > 0 ? $" · {a.Leftovers.Count} need alias" : ""));
            }
        }

        if (ui is not null)
            ui.Post(Raise);
        else
            Raise();
    }
}

public sealed record InstalledDepsRepairOutcome(InstalledDepsAnalysis Analysis, MissingLogActivation Activation);

public sealed record InstalledDepsRepairJob(JobHandle Handle, Task<InstalledDepsRepairOutcome> Result);
