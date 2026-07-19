using VarVault.Common;

namespace VarVault.Domain.Indexing;

/// <summary>
/// An immutable ordered list of package ids for one (filter, sort) — the gallery/TreeDataGrid binds to
/// it for O(1) <c>rows[i]</c> and true scrollbar-jump without deep OFFSET queries. Rebuilt when the
/// filter/sort changes (per-session, decision D3). (Data-arch §5.8, checklist 1.39.)
/// </summary>
public sealed class OrderedSnapshot(IReadOnlyList<long> orderedPackageIds)
{
    private readonly IReadOnlyList<long> _ids = Guard.NotNull(orderedPackageIds);

    public int Count => _ids.Count;

    /// <summary>O(1) random access to the id at a row position.</summary>
    public long this[int index] => _ids[index];

    /// <summary>Position of a package id, or -1. (Linear — used sparingly, e.g. reveal-in-list.)</summary>
    public int PositionOf(long packageId)
    {
        for (var i = 0; i < _ids.Count; i++)
        {
            if (_ids[i] == packageId)
                return i;
        }
        return -1;
    }
}
