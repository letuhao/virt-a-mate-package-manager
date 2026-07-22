using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-4 · Fix encoding → UTF-8 (new var, original retained). Queues via jobs panel when a runner is available. (16-checklist DLG-4.)</summary>
public sealed partial class FixEncodingViewModel(
    IHealthService health,
    Services.EncodingFixJobRunner? jobs = null) : ObservableObject
{
    [ObservableProperty] private long _varFileId;
    [ObservableProperty] private string? _codepage;
    [ObservableProperty] private string _before = "Custom/è¡£è£…/ã‚¹ã‚«ãƒ¼ãƒˆ.vam";
    [ObservableProperty] private string _after = "Custom/衣装/スカート.vam";
    [ObservableProperty] private string? _resultMessage;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Also slim the rewritten var (drop redundant embedded content) while fixing encoding. (AC-28)</summary>
    [ObservableProperty] private bool _alsoSlim;

    /// <summary>
    /// Queue (or run) encoding fix for a group (Health "Fix GBK…" / "Fix group…") or a single var when
    /// <see cref="VarFileId"/> is set. Group mode is VarFileId ≤ 0 only — Codepage alone must not force a group fix.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFix))]
    public async Task FixAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ResultMessage = null;
        try
        {
            // Group when opened from Health (id 0); single-var when Library/detail sets VarFileId.
            var useGroup = VarFileId <= 0;
            if (jobs is not null)
            {
                _ = useGroup
                    ? jobs.StartGroup(Codepage)
                    : jobs.StartVarFiles([VarFileId]);
                ResultMessage = "Queued — watch the jobs panel (originals retained as .fixed.var).";
                return;
            }

            // Test / headless path without a job runner.
            if (useGroup)
            {
                var batch = await health.FixGroupAsync(Codepage, cancellationToken).ConfigureAwait(true);
                ResultMessage = batch.Failed == 0
                    ? $"Fixed {batch.Succeeded} → UTF-8 (originals retained)"
                    : $"Fixed {batch.Succeeded}, failed {batch.Failed}";
                return;
            }

            var result = await health.FixAsync(VarFileId, cancellationToken).ConfigureAwait(true);
            ResultMessage = result.IsSuccess ? "Fixed → UTF-8 (original retained)" : result.Error.Message;
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            ResultMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanFix() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => FixCommand.NotifyCanExecuteChanged();
}
