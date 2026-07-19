namespace VarVault.Domain.Analyzer;

/// <summary>
/// Whether a repository has room for a file without breaching its <c>MinFreeBytes</c> reserve:
/// <c>free − minFree ≥ size</c>. Gates placement/migration so a drive is never filled past its reserve.
/// (Checklist 1.8.)
/// </summary>
public static class PlacementCapacity
{
    public static bool HasRoom(long? freeBytes, long minFreeBytes, long sizeBytes) =>
        freeBytes is { } free && free - minFreeBytes >= sizeBytes;
}
