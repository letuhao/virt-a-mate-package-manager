using VarVault.Common;

namespace VarVault.Domain.Import;

/// <summary>Which copy to keep for a same-version content conflict. Domain-local; Infra maps to <c>Sdk.Import.ImportDecision</c>.</summary>
public enum KeepChoice { KeepIncoming, KeepExisting, KeepBoth }

/// <summary>The signals of one side of a conflict, reduced to what the recommender weighs. (doc 30 §4.)</summary>
public sealed record ConflictSide(
    bool Valid,           // integrity Ok (not corrupt, not missing-meta)
    bool MetaDivergent,   // meta.json creator/package differs from the filename's
    int GbkEntryCount,    // legacy-CJK entry names still present
    int EntryCount,
    long SizeBytes,
    DateTime FileMtime);

/// <summary>A recommendation + the human reason behind it. (doc 30 §4.)</summary>
public sealed record ConflictRecommendation(KeepChoice Choice, string Reason);

/// <summary>
/// Advisory "which is correct?" for a same-version content conflict. Deterministic priority ladder — the first
/// decisive signal wins; mtime is the weakest (a download date is not authoritative). Pure. (doc 30 §4.)
/// </summary>
public static class ConflictRecommender
{
    // A size gap only counts when entry counts tie — guards a truncated download that kept the same entry list.
    private const double MaterialSizeFraction = 0.05;

    public static ConflictRecommendation Recommend(ConflictSide incoming, ConflictSide existing)
    {
        Guard.NotNull(incoming);
        Guard.NotNull(existing);

        // 7 · both broken — never overwrite one bad copy with another.
        if (!incoming.Valid && !existing.Valid)
            return new(KeepChoice.KeepExisting, "Both copies are corrupt — keep the repo copy and check manually.");

        // 1 · validity (the most common real case).
        if (incoming.Valid && !existing.Valid)
            return new(KeepChoice.KeepIncoming, "The repo copy is corrupt / missing meta — keep the incoming (readable) copy.");
        if (!incoming.Valid && existing.Valid)
            return new(KeepChoice.KeepExisting, "The incoming copy is corrupt / missing meta — keep the repo copy.");

        // 2 · meta identity (a divergent meta.json is a bad sign).
        if (!incoming.MetaDivergent && existing.MetaDivergent)
            return new(KeepChoice.KeepIncoming, "The repo copy's meta.json declares the wrong identity — keep the incoming copy (meta matches).");
        if (incoming.MetaDivergent && !existing.MetaDivergent)
            return new(KeepChoice.KeepExisting, "The incoming copy's meta.json declares the wrong identity — keep the repo copy.");

        // 3 · encoding health — prefer the Unicode-clean copy of otherwise-equal content.
        if (incoming.GbkEntryCount == 0 && existing.GbkEntryCount > 0)
            return new(KeepChoice.KeepIncoming, "The repo copy still has GBK-encoded names — keep the incoming (Unicode-clean) copy.");
        if (incoming.GbkEntryCount > 0 && existing.GbkEntryCount == 0)
            return new(KeepChoice.KeepExisting, "The incoming copy still has GBK-encoded names — keep the repo (Unicode-clean) copy.");

        // 4 · fuller wins — more entries, or (entries tied) a materially larger payload (truncation guard).
        if (incoming.EntryCount != existing.EntryCount)
            return incoming.EntryCount > existing.EntryCount
                ? new(KeepChoice.KeepIncoming, $"The incoming copy has {incoming.EntryCount - existing.EntryCount} more entries — keep it.")
                : new(KeepChoice.KeepExisting, $"The repo copy has {existing.EntryCount - incoming.EntryCount} more entries — keep it.");
        if (MateriallyLarger(incoming.SizeBytes, existing.SizeBytes))
            return new(KeepChoice.KeepIncoming, "Same entry count but the incoming copy is materially larger (the repo copy may be truncated) — keep the incoming.");
        if (MateriallyLarger(existing.SizeBytes, incoming.SizeBytes))
            return new(KeepChoice.KeepExisting, "Same entry count but the repo copy is materially larger (the incoming may be truncated) — keep the repo copy.");

        // 5 · mtime — weakest tiebreak only, and explicitly a download date.
        if (incoming.FileMtime > existing.FileMtime)
            return new(KeepChoice.KeepIncoming, "Contents look equivalent — keep the incoming copy (newer file date; download date, for reference).");
        if (existing.FileMtime > incoming.FileMtime)
            return new(KeepChoice.KeepExisting, "Contents look equivalent — keep the repo copy (newer file date; download date, for reference).");

        // 6 · genuinely ambiguous — both valid, both meta-ok, similar size, real content diff → keep both, user decides.
        return new(KeepChoice.KeepBoth, "Both are valid but differ in content — keep both (rename the incoming); you decide.");
    }

    private static bool MateriallyLarger(long a, long b) =>
        b > 0 && a > b && (a - b) >= (long)(b * MaterialSizeFraction);
}
