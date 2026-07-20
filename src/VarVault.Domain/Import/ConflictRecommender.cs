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
            return new(KeepChoice.KeepExisting, "Cả hai bản đều hỏng — giữ bản trong repo và kiểm tra thủ công.");

        // 1 · validity (the most common real case).
        if (incoming.Valid && !existing.Valid)
            return new(KeepChoice.KeepIncoming, "Bản trong repo hỏng/thiếu meta — giữ bản mới (đọc được).");
        if (!incoming.Valid && existing.Valid)
            return new(KeepChoice.KeepExisting, "Bản mới hỏng/thiếu meta — giữ bản trong repo.");

        // 2 · meta identity (a divergent meta.json is a bad sign).
        if (!incoming.MetaDivergent && existing.MetaDivergent)
            return new(KeepChoice.KeepIncoming, "Bản trong repo có meta.json khai sai tên — giữ bản mới (meta khớp).");
        if (incoming.MetaDivergent && !existing.MetaDivergent)
            return new(KeepChoice.KeepExisting, "Bản mới có meta.json khai sai tên — giữ bản trong repo.");

        // 3 · encoding health — prefer the Unicode-clean copy of otherwise-equal content.
        if (incoming.GbkEntryCount == 0 && existing.GbkEntryCount > 0)
            return new(KeepChoice.KeepIncoming, "Bản trong repo còn tên mã GBK — giữ bản mới đã đúng Unicode.");
        if (incoming.GbkEntryCount > 0 && existing.GbkEntryCount == 0)
            return new(KeepChoice.KeepExisting, "Bản mới còn tên mã GBK — giữ bản trong repo đã đúng Unicode.");

        // 4 · fuller wins — more entries, or (entries tied) a materially larger payload (truncation guard).
        if (incoming.EntryCount != existing.EntryCount)
            return incoming.EntryCount > existing.EntryCount
                ? new(KeepChoice.KeepIncoming, $"Bản mới có nhiều hơn {incoming.EntryCount - existing.EntryCount} entry — giữ bản mới.")
                : new(KeepChoice.KeepExisting, $"Bản trong repo có nhiều hơn {existing.EntryCount - incoming.EntryCount} entry — giữ bản repo.");
        if (MateriallyLarger(incoming.SizeBytes, existing.SizeBytes))
            return new(KeepChoice.KeepIncoming, "Cùng số entry nhưng bản mới lớn hơn rõ rệt (bản repo có thể bị cắt cụt) — giữ bản mới.");
        if (MateriallyLarger(existing.SizeBytes, incoming.SizeBytes))
            return new(KeepChoice.KeepExisting, "Cùng số entry nhưng bản repo lớn hơn rõ rệt (bản mới có thể bị cắt cụt) — giữ bản repo.");

        // 5 · mtime — weakest tiebreak only, and explicitly a download date.
        if (incoming.FileMtime > existing.FileMtime)
            return new(KeepChoice.KeepIncoming, "Nội dung tương đương — giữ bản mới (ngày file mới hơn; ngày tải, tham khảo).");
        if (existing.FileMtime > incoming.FileMtime)
            return new(KeepChoice.KeepExisting, "Nội dung tương đương — giữ bản trong repo (ngày file mới hơn; ngày tải, tham khảo).");

        // 6 · genuinely ambiguous — both valid, both meta-ok, similar size, real content diff → keep both, user decides.
        return new(KeepChoice.KeepBoth, "Cả hai đều hợp lệ nhưng khác nội dung — giữ cả hai (đổi tên bản mới), bạn tự quyết.");
    }

    private static bool MateriallyLarger(long a, long b) =>
        b > 0 && a > b && (a - b) >= (long)(b * MaterialSizeFraction);
}
