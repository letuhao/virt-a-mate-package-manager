using VarVault.Domain.Import;

namespace VarVault.Common.Tests;

/// <summary>Unit coverage for the conflict recommender priority ladder (doc 31 Phase 2; spec §4).</summary>
public class ConflictRecommenderTests
{
    private static readonly DateTime T2023 = new(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2024 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ConflictSide Side(bool valid = true, bool metaDiv = false, int gbk = 0, int entries = 100,
        long size = 10_000_000, DateTime? mtime = null) => new(valid, metaDiv, gbk, entries, size, mtime ?? T2023);

    [Fact]
    public void Existing_corrupt_keeps_incoming()
    {
        var r = ConflictRecommender.Recommend(incoming: Side(valid: true), existing: Side(valid: false));
        Assert.Equal(KeepChoice.KeepIncoming, r.Choice);
        Assert.Contains("hỏng", r.Reason);
    }

    [Fact]
    public void Incoming_corrupt_keeps_existing()
        => Assert.Equal(KeepChoice.KeepExisting,
            ConflictRecommender.Recommend(Side(valid: false), Side(valid: true)).Choice);

    [Fact]
    public void Both_broken_keeps_existing_for_manual()
    {
        var r = ConflictRecommender.Recommend(Side(valid: false), Side(valid: false));
        Assert.Equal(KeepChoice.KeepExisting, r.Choice);
        Assert.Contains("Cả hai", r.Reason);
    }

    [Fact]
    public void Existing_meta_divergent_keeps_incoming()
        => Assert.Equal(KeepChoice.KeepIncoming,
            ConflictRecommender.Recommend(Side(metaDiv: false), Side(metaDiv: true)).Choice);

    [Fact]
    public void Existing_gbk_keeps_incoming_clean()
        => Assert.Equal(KeepChoice.KeepIncoming,
            ConflictRecommender.Recommend(Side(gbk: 0), Side(gbk: 7)).Choice);

    [Fact]
    public void More_entries_keeps_the_fuller_one()
    {
        var r = ConflictRecommender.Recommend(Side(entries: 137), Side(entries: 41));
        Assert.Equal(KeepChoice.KeepIncoming, r.Choice);
        Assert.Contains("entry", r.Reason);
    }

    [Fact]
    public void Same_entries_materially_larger_wins()
        => Assert.Equal(KeepChoice.KeepIncoming,
            ConflictRecommender.Recommend(Side(entries: 100, size: 20_000_000), Side(entries: 100, size: 8_000_000)).Choice);

    [Fact]
    public void Mtime_is_only_a_weak_tiebreak()
    {
        // Everything else equal -> newer file wins, but only here at the bottom of the ladder.
        var r = ConflictRecommender.Recommend(Side(mtime: T2024), Side(mtime: T2023));
        Assert.Equal(KeepChoice.KeepIncoming, r.Choice);
        Assert.Contains("tham khảo", r.Reason);
    }

    [Fact]
    public void Validity_outranks_mtime()
    {
        // Incoming is older but valid; existing is newer but corrupt -> validity wins, not mtime.
        var r = ConflictRecommender.Recommend(Side(valid: true, mtime: T2023), Side(valid: false, mtime: T2024));
        Assert.Equal(KeepChoice.KeepIncoming, r.Choice);
    }

    [Fact]
    public void Truly_ambiguous_recommends_keep_both()
    {
        var r = ConflictRecommender.Recommend(Side(), Side());
        Assert.Equal(KeepChoice.KeepBoth, r.Choice);
    }
}
