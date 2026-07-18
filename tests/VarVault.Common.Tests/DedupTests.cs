using VarVault.Domain.Dedup;
using VarVault.TestKit;

namespace VarVault.Common.Tests;

[Trait("Category", TestCategories.Unit)]
public class DeletionPredicateTests
{
    private static VarFileDedupFacts Copy(long id, string identity, string? hash, bool online, string sig = "sig") =>
        new(id, identity, sig, hash, online);

    [Fact]
    public void Allows_delete_when_a_verified_online_duplicate_exists()
    {
        var candidate = Copy(1, "A.B.1", "hashX", online: true);
        var group = new[] { candidate, Copy(2, "A.B.1", "hashX", online: true) };
        Assert.True(DeletionPredicate.Evaluate(candidate, group).CanDelete);
    }

    [Fact]
    public void Blocks_when_candidate_hash_not_computed()
    {
        var candidate = Copy(1, "A.B.1", hash: null, online: true);
        var group = new[] { candidate, Copy(2, "A.B.1", "hashX", online: true) };
        var v = DeletionPredicate.Evaluate(candidate, group);
        Assert.False(v.CanDelete);
        Assert.Equal(DeletionBlockReason.HashNotVerified, v.Reason);
    }

    [Fact]
    public void Blocks_single_online_copy()
    {
        // 4.3 — the only online copy can never be deleted (the offline sibling is not a safety net).
        var candidate = Copy(1, "A.B.1", "hashX", online: true);
        var group = new[] { candidate, Copy(2, "A.B.1", "hashX", online: false) };
        var v = DeletionPredicate.Evaluate(candidate, group);
        Assert.False(v.CanDelete);
        Assert.Equal(DeletionBlockReason.NoVerifiedOnlineDuplicate, v.Reason);
    }

    [Fact]
    public void Blocks_when_online_duplicate_hash_differs()
    {
        var candidate = Copy(1, "A.B.1", "hashX", online: true);
        var group = new[] { candidate, Copy(2, "A.B.1", "hashY", online: true) };
        Assert.False(DeletionPredicate.Evaluate(candidate, group).CanDelete);
    }

    [Fact]
    public void Cross_identity_copy_is_never_a_safety_net()
    {
        // 4.2 — same content, different identity → not a delete authorization.
        var candidate = Copy(1, "Alice.X.1", "hashX", online: true);
        var group = new[] { candidate, Copy(2, "Bob.X.1", "hashX", online: true) };
        Assert.False(DeletionPredicate.Evaluate(candidate, group).CanDelete);
    }
}

[Trait("Category", TestCategories.Unit)]
public class DedupGroupingTests
{
    private static VarFileDedupFacts F(long id, string identity, string sig, bool online = true) =>
        new(id, identity, sig, null, online);

    [Fact]
    public void Groups_duplicates_within_one_identity()
    {
        var analysis = DedupGrouping.Analyze(
        [
            F(1, "A.B.1", "sig1"),
            F(2, "A.B.1", "sig1"), // duplicate of 1
            F(3, "A.B.1", "sig2"), // different content, same identity → content conflict, not a dup group
        ]);

        var group = Assert.Single(analysis.WithinIdentity);
        Assert.Equal("sig1", group.ContentSignature);
        Assert.Equal(2, group.Members.Count);
    }

    [Fact]
    public void Same_content_across_identities_is_report_only()
    {
        // 4.2 — reported, never a within-identity delete group.
        var analysis = DedupGrouping.Analyze(
        [
            F(1, "Alice.X.1", "sig1"),
            F(2, "Bob.X.1", "sig1"),
        ]);

        Assert.Empty(analysis.WithinIdentity);
        var cross = Assert.Single(analysis.CrossIdentity);
        Assert.Equal(2, cross.Members.Count);
    }

    [Fact]
    public void Group_with_offline_member_is_flagged_unsafe()
    {
        // 4.4 — any offline member blocks automated dedup.
        var analysis = DedupGrouping.Analyze(
        [
            F(1, "A.B.1", "sig1", online: true),
            F(2, "A.B.1", "sig1", online: false),
        ]);
        Assert.False(analysis.WithinIdentity.Single().AllOnline);
    }

    [Fact]
    public void Copies_without_a_signature_are_ignored()
    {
        var analysis = DedupGrouping.Analyze([new VarFileDedupFacts(1, "A.B.1", null, null, true)]);
        Assert.Empty(analysis.WithinIdentity);
    }
}
