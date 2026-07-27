namespace VarVault.Domain.Content;

/// <summary>One zip entry that participates in a VaM-normalized path collision.</summary>
public sealed record EntryCollisionMember(string FullName, long UncompressedSize, uint Crc32);

/// <summary>
/// A group of entries that collapse to the same VaM key (<see cref="VamLoadDefectDetector.NormalizeEntryKey"/>).
/// <see cref="SuggestedKeepFullName"/> is the keep-larger default (first on ties).
/// </summary>
public sealed record EntryCollisionGroup(
    string NormalizedKey,
    IReadOnlyList<EntryCollisionMember> Members,
    string SuggestedKeepFullName);
