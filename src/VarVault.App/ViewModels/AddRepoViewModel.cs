using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;
using VarVault.Sdk.Repositories;
using VarVault.Sdk.Threading;

namespace VarVault.App.ViewModels;

/// <summary>DLG-2 · Add repository: register a folder, show detected media/tier. (16-checklist DLG-2.)</summary>
public sealed partial class AddRepoViewModel(
    IRepositoryService repositories,
    Action? onAdded = null,
    IMigrationService? migration = null,
    IJobQueue? jobs = null) : ObservableObject
{
    private static readonly Regex ReservePattern = new(
        @"^\s*(?<n>[\d]+(?:\.\d+)?)\s*(?<u>TB|GB|MB|KB|B)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [ObservableProperty] private string? _folderPath;
    [ObservableProperty] private string? _reserve = "200 GB";
    [ObservableProperty] private RepositoryInfo? _registered;
    [ObservableProperty] private string? _message;

    /// <summary>Whether to also rebalance existing data onto this drive (prototype checkbox). (GE-2)</summary>
    [ObservableProperty] private bool _rebalanceExisting;

    /// <summary>Preferred tier options (the profiler auto-detects, but the user can hint). (AC-26)</summary>
    public IReadOnlyList<string> TierOptions { get; } = ["Auto (detect)", "T1 (hot)", "T2 (warm)", "T3 (cold)"];
    [ObservableProperty] private string _preferredTier = "Auto (detect)";

    /// <summary>Optional folder-picker hook the view sets to the real StorageProvider; returns a chosen path. (AC-26)</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>"Browse…" → pick a folder via the host picker and fill the path. (AC-26)</summary>
    [RelayCommand]
    public async Task BrowseAsync()
    {
        if (FolderPicker is null)
            return;
        var picked = await FolderPicker().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(picked))
            FolderPath = picked;
    }

    [RelayCommand]
    public async Task AddAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(FolderPath))
            return;

        long? minFree = null;
        if (!string.IsNullOrWhiteSpace(Reserve))
        {
            minFree = ParseReserveBytes(Reserve);
            if (minFree is null)
            {
                Message = $"Couldn't parse reserve “{Reserve}”. Use e.g. 200 GB or leave blank.";
                return;
            }
        }

        var request = new RegisterRepositoryRequest(
            DeriveName(FolderPath!),
            FolderPath!,
            MinFreeBytes: minFree,
            PreferredTier: ParsePreferredTier(PreferredTier));

        var r = await repositories.RegisterAsync(request, cancellationToken).ConfigureAwait(true);
        if (!r.IsSuccess)
        {
            Message = r.Error.Message;
            return;
        }

        Registered = r.Value;
        Message = $"Detected {r.Value.MediaType} → Tier {r.Value.Tier}";

        if (RebalanceExisting && migration is not null)
        {
            var repoId = r.Value.Id;
            var name = r.Value.Name;
            if (jobs is not null)
            {
                jobs.Enqueue($"Rebalance onto {name}", async ctx =>
                {
                    await migration.RebalanceOntoRepositoryAsync(repoId, ctx.Cancellation).ConfigureAwait(false);
                });
                Message += " · Rebalance queued.";
            }
            else
            {
                // Tests / hosts without a job queue — run inline.
                var result = await migration.RebalanceOntoRepositoryAsync(repoId, cancellationToken).ConfigureAwait(true);
                Message += result.Moved + result.Failed > 0
                    ? $" · Rebalanced {result.Moved} moved, {result.Failed} failed."
                    : " · No packages to rebalance.";
            }
        }

        onAdded?.Invoke(); // GA-5/GA-6: kick off indexing of the new repo
    }

    /// <summary>Repo display name = the folder's leaf name (e.g. <c>VarVault_test_repo</c>), not a generic literal.
    /// Falls back to "repository" for a drive root / empty leaf. (28-checklist A3.)</summary>
    internal static string DeriveName(string folderPath)
    {
        var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(leaf) ? "repository" : leaf;
    }

    public static long? ParseReserveBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var m = ReservePattern.Match(text);
        if (!m.Success)
            return null;
        if (!double.TryParse(m.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0)
            return null;
        var unit = m.Groups["u"].Success ? m.Groups["u"].Value.ToUpperInvariant() : "GB";
        var mul = unit switch
        {
            "TB" => 1024L * 1024 * 1024 * 1024,
            "GB" => 1024L * 1024 * 1024,
            "MB" => 1024L * 1024,
            "KB" => 1024L,
            _ => 1L,
        };
        var bytes = (long)(n * mul);
        return bytes > 0 ? bytes : null;
    }

    public static int? ParsePreferredTier(string? text) => text switch
    {
        "T1 (hot)" => 1,
        "T2 (warm)" => 2,
        "T3 (cold)" => 3,
        _ => null,
    };
}
