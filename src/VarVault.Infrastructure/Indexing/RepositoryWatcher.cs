using System.IO;
using VarVault.Common;

namespace VarVault.Infrastructure.Indexing;

/// <summary>
/// Watches a repository for <c>.var</c> changes and raises <see cref="VarChanged"/> (create/modify/
/// delete/rename), so the indexer can incrementally re-index just what changed. The changed path is
/// reported; the caller debounces and schedules the incremental scan. (Checklist 1.30.)
/// </summary>
public sealed class RepositoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    /// <summary>Raised with the full path of a changed <c>.var</c>.</summary>
    public event Action<string>? VarChanged;

    public RepositoryWatcher(string repositoryRoot)
    {
        Guard.NotNullOrWhiteSpace(repositoryRoot);
        _watcher = new FileSystemWatcher(repositoryRoot, "*.var")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => VarChanged?.Invoke(e.FullPath);
        _watcher.Changed += (_, e) => VarChanged?.Invoke(e.FullPath);
        _watcher.Deleted += (_, e) => VarChanged?.Invoke(e.FullPath);
        _watcher.Renamed += (_, e) => VarChanged?.Invoke(e.FullPath);
    }

    /// <summary>Begin/stop raising events.</summary>
    public bool Enabled
    {
        get => _watcher.EnableRaisingEvents;
        set => _watcher.EnableRaisingEvents = value;
    }

    public void Dispose() => _watcher.Dispose();
}
