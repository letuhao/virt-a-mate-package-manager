namespace VarVault.Domain.Indexing;

/// <summary>
/// Tracks which packages' derived <see cref="Entities.PackageListItem"/> rows are stale because a base
/// table (VarFile / Package / counts / usage) changed under them. A base-table write marks the owning
/// package dirty; draining the set feeds <see cref="ICatalogStore.RefreshReadModelAsync"/> so only the
/// affected read-model rows are recomputed — never the whole table. Thread-safe. (Checklist 0.26.)
/// </summary>
public sealed class ReadModelDirtySet
{
    private readonly HashSet<long> _packages = [];
    private readonly Lock _gate = new();

    /// <summary>Mark a package's read-model row stale (e.g. after inserting one of its VarFiles).</summary>
    public void MarkPackage(long packageId)
    {
        lock (_gate)
            _packages.Add(packageId);
    }

    /// <summary>Mark a package dirty only when the id is known (convenience for nullable upsert results).</summary>
    public void MarkPackage(long? packageId)
    {
        if (packageId is { } id)
            MarkPackage(id);
    }

    /// <summary>Pending dirty-package count.</summary>
    public int Count
    {
        get { lock (_gate) return _packages.Count; }
    }

    /// <summary>Atomically take and clear the current dirty set — the batch to refresh.</summary>
    public IReadOnlyCollection<long> Drain()
    {
        lock (_gate)
        {
            if (_packages.Count == 0)
                return [];
            var batch = _packages.ToArray();
            _packages.Clear();
            return batch;
        }
    }

    /// <summary>Drain the dirty set and refresh exactly those read-model rows through the store.</summary>
    public async Task FlushAsync(ICatalogStore store, CancellationToken cancellationToken = default)
    {
        Common.Guard.NotNull(store);
        var batch = Drain();
        if (batch.Count > 0)
            await store.RefreshReadModelAsync(batch, cancellationToken).ConfigureAwait(false);
    }
}
