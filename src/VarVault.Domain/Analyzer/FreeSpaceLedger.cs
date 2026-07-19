using System.Collections.Concurrent;
using VarVault.Common;

namespace VarVault.Domain.Analyzer;

/// <summary>
/// ⚠ Tracks in-flight space reservations per repository so concurrent migration jobs can't collectively
/// overfill a drive past its <c>MinFreeBytes</c>. A reservation succeeds only if
/// <c>free − alreadyReserved − requested ≥ minFree</c>. Thread-safe. (Checklist 5.10, BE-P3.)
/// </summary>
public sealed class FreeSpaceLedger
{
    private readonly ConcurrentDictionary<Guid, long> _reserved = new();

    /// <summary>Try to reserve <paramref name="bytes"/> on a repo; returns false if it would breach MinFree.</summary>
    public bool TryReserve(Guid repositoryId, long bytes, long freeBytes, long minFreeBytes)
    {
        Guard.Positive((int)Math.Min(bytes, int.MaxValue)); // bytes must be > 0
        while (true)
        {
            var current = _reserved.GetValueOrDefault(repositoryId);
            if (freeBytes - current - bytes < minFreeBytes)
                return false;

            var updated = current + bytes;
            if (_reserved.TryUpdate(repositoryId, updated, current))
                return true;
            if (current == 0 && _reserved.TryAdd(repositoryId, updated))
                return true;
            // lost a race — retry
        }
    }

    /// <summary>Release a prior reservation (on completion or failure).</summary>
    public void Release(Guid repositoryId, long bytes)
    {
        while (true)
        {
            var current = _reserved.GetValueOrDefault(repositoryId);
            var updated = Math.Max(0, current - bytes);
            if (current == 0 || _reserved.TryUpdate(repositoryId, updated, current))
                return;
        }
    }

    /// <summary>Bytes currently reserved on a repo (for diagnostics/tests).</summary>
    public long Reserved(Guid repositoryId) => _reserved.GetValueOrDefault(repositoryId);
}
