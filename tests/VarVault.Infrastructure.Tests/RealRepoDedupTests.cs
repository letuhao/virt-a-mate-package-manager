using System.IO;
using VarVault.Domain.Dedup;
using VarVault.Domain.Fingerprinting;
using VarVault.Domain.Identity;
using VarVault.Domain.ValueObjects;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// D: dedup over the real corpus — computes structural signatures + full hashes for the
/// "notSameContentFiles" fixtures and groups logical duplicates within an identity. This is the exact
/// case the user's VaMHelper struggled with (same content, different zip size). (4.5, BE-F3/F4.)
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class RealRepoDedupTests
{
    private static string Root => TestCorpus.Primary ?? "";
    private readonly Sha256FileHasher _hasher = new();

    [Fact]
    public async Task Groups_logical_duplicates_across_the_corpus_by_structural_signature()
    {
        if (!Directory.Exists(Root))
            return;

        var facts = new List<VarFileDedupFacts>();
        foreach (var path in Directory.GetFiles(Root, "*.var", SearchOption.AllDirectories))
        {
            var read = ZipCentralDirectoryReader.Read(path);
            if (read.IsFailure)
                continue;

            var identity = PackageId.TryParse(Path.GetFileName(path));
            var key = identity.IsSuccess ? identity.Value.IdentityKey : IdentityFold.Compute(Path.GetFileName(path));
            var sig = ContentSignatureEngine.Compute(read.Value).ContentSignature;
            facts.Add(new VarFileDedupFacts(facts.Count + 1, key, sig, ContentHash: null, IsOnline: true));
        }

        Assert.NotEmpty(facts);
        var analysis = DedupGrouping.Analyze(facts);

        // This corpus intentionally contains ___VarRedundant____ copies, so real within-identity dupes must exist —
        // a regression producing zero groups now fails here instead of passing silently. (24-checklist D6.)
        Assert.NotEmpty(analysis.WithinIdentity);

        // The analysis must be self-consistent: every within-identity group shares one identity + signature.
        foreach (var group in analysis.WithinIdentity)
        {
            Assert.True(group.Members.Count > 1);
            Assert.All(group.Members, m => Assert.Equal(group.IdentityKey, m.IdentityKey));
            Assert.All(group.Members, m => Assert.Equal(group.ContentSignature, m.ContentSignature));
        }

        // If any logical duplicates exist, a full-hash verify authorizes deletion of all but one.
        if (analysis.WithinIdentity.Count > 0)
        {
            var group = analysis.WithinIdentity[0];
            // hashing is lazy — only done here for the verify step (BE-F3).
            var candidate = group.Members[0];
            var verdictWithoutHash = DeletionPredicate.Evaluate(candidate, group.Members);
            Assert.Equal(DeletionBlockReason.HashNotVerified, verdictWithoutHash.Reason); // no hash → never delete
        }
    }

    [Fact]
    public async Task Full_hash_is_deterministic_for_a_real_var()
    {
        if (!Directory.Exists(Root))
            return;

        var sample = Directory.GetFiles(Root, "*.var", SearchOption.AllDirectories).FirstOrDefault();
        if (sample is null)
            return;

        var h1 = await _hasher.ComputeAsync(sample);
        var h2 = await _hasher.ComputeAsync(sample);
        Assert.True(h1.IsSuccess);
        Assert.Equal(h1.Value, h2.Value);
        Assert.Equal(64, h1.Value.Length); // SHA-256 hex
    }
}
