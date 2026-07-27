using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>One collision group in the dedup picker; SelectedKeep preselects keep-larger.</summary>
public sealed partial class DedupCollisionGroupViewModel : ObservableObject
{
    public DedupCollisionGroupViewModel(DuplicateEntryCollision collision)
    {
        NormalizedKey = collision.NormalizedKey;
        Candidates = collision.Candidates;
        SelectedKeep = Candidates.FirstOrDefault(c =>
            string.Equals(c.FullName, collision.SuggestedKeepFullName, StringComparison.Ordinal))
            ?? Candidates.FirstOrDefault();
    }

    public string NormalizedKey { get; }
    public IReadOnlyList<DuplicateEntryCandidate> Candidates { get; }
    [ObservableProperty] private DuplicateEntryCandidate? _selectedKeep;
}

/// <summary>
/// Interactive per-collision picker for VaM DuplicateEntries repair. Default selection is keep-larger;
/// Apply writes a <c>.dedup.var</c> sibling via <see cref="IHealthService.FixDuplicateEntriesAsync"/>.
/// </summary>
public sealed partial class DedupFixViewModel(
    IHealthService health,
    Action? close = null,
    Action? onFixed = null) : ObservableObject
{
    public ObservableCollection<DedupCollisionGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private long _varFileId;
    [ObservableProperty] private string _varName = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isLoaded;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (VarFileId <= 0)
        {
            StatusMessage = "No var selected.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading collisions…";
        try
        {
            var result = await health.GetDuplicateEntryCollisionsAsync(VarFileId, cancellationToken)
                .ConfigureAwait(true);
            Groups.Clear();
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                IsLoaded = false;
                return;
            }

            foreach (var g in result.Value)
                Groups.Add(new DedupCollisionGroupViewModel(g));

            IsLoaded = Groups.Count > 0;
            StatusMessage = Groups.Count == 0
                ? "No collisions found (catalog may be stale — re-scan)."
                : $"{Groups.Count} collision group(s). Keep-larger is preselected.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApply())
            return;

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in Groups)
            {
                if (g.SelectedKeep is null)
                {
                    StatusMessage = $"Pick a survivor for key '{g.NormalizedKey}'.";
                    return;
                }

                choices[g.NormalizedKey] = g.SelectedKeep.FullName;
            }

            var result = await health.FixDuplicateEntriesAsync(VarFileId, choices, cancellationToken)
                .ConfigureAwait(true);
            if (result.IsFailure)
            {
                StatusMessage = result.Error.Message;
                return;
            }

            StatusMessage = "Fixed → .dedup.var (original retained).";
            onFixed?.Invoke();
            close?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ApplyCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanApply() => !IsBusy && IsLoaded && Groups.Count > 0;

    partial void OnIsBusyChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();
    partial void OnIsLoadedChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();
}
