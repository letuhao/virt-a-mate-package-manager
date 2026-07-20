using VarVault.Domain.Entities;
using VarVault.Domain.Import;

namespace VarVault.Common.Tests;

/// <summary>Unit coverage for the import lane classifier (doc 31 Phase 1; spec §3, rules 1 &amp; 2, E1/D1).</summary>
public class ImportClassifierTests
{
    private static CatalogVarFacts Cat(string id, string sig) => new(id, sig);
    private static readonly IReadOnlyList<CatalogVarFacts> Repo =
    [
        Cat("creator.pack.1", "SIG_PACK1"),
        Cat("creator.pack.2", "SIG_PACK2"),
        Cat("other.thing.5", "SIG_OTHER5"),
    ];

    private static ImportCandidateFacts Cand(
        IntegrityStatus integrity = IntegrityStatus.Ok, string? sig = "SIG_NEW",
        string? filenameId = "brand.new.1", bool metaDivergent = false, bool encoding = false) =>
        new(integrity, sig, filenameId, metaDivergent, encoding);

    [Fact]
    public void Corrupt_zip_is_Corrupt()
        => Assert.Equal(LaneKind.Corrupt, ImportClassifier.Classify(Cand(integrity: IntegrityStatus.CorruptZip), Repo));

    [Fact]
    public void Missing_meta_is_Corrupt()
        => Assert.Equal(LaneKind.Corrupt, ImportClassifier.Classify(Cand(integrity: IntegrityStatus.MissingMeta), Repo));

    [Fact]
    public void Exact_content_anywhere_is_Exact_even_if_misnamed()
    {
        // Same ContentSignature as a repo var but a totally different (garbage) filename -> still Exact (E1/D1).
        var c = Cand(sig: "SIG_PACK1", filenameId: null, metaDivergent: true);
        Assert.Equal(LaneKind.Exact, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void Same_version_different_content_is_Conflict()
    {
        var c = Cand(sig: "SIG_DIFFERENT", filenameId: "creator.pack.1");
        Assert.Equal(LaneKind.Conflict, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void Different_version_is_New_not_upgrade()
    {
        // Repo has creator.pack .1 and .2; candidate is .3 with new content -> New (rule 1: never compare versions).
        var c = Cand(sig: "SIG_PACK3", filenameId: "creator.pack.3");
        Assert.Equal(LaneKind.New, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void Divergent_name_not_in_library_is_Naming()
    {
        // filename parses but meta disagrees, and neither content nor filename-identity is in the repo.
        var c = Cand(sig: "SIG_NEW", filenameId: "wrong.name.1", metaDivergent: true);
        Assert.Equal(LaneKind.Naming, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void Unparseable_name_valid_var_is_Naming()
    {
        var c = Cand(sig: "SIG_NEW", filenameId: null, metaDivergent: false);
        Assert.Equal(LaneKind.Naming, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void New_valid_content_with_gbk_names_is_Cjk()
    {
        var c = Cand(sig: "SIG_NEW", filenameId: "gbk.creator.1", encoding: true);
        Assert.Equal(LaneKind.Cjk, ImportClassifier.Classify(c, Repo));
    }

    [Fact]
    public void Brand_new_clean_var_is_New()
        => Assert.Equal(LaneKind.New, ImportClassifier.Classify(Cand(), Repo));

    [Fact]
    public void Precedence_corrupt_beats_everything()
    {
        // Even an exact-content match: if the incoming zip is corrupt, it's Corrupt.
        var c = Cand(integrity: IntegrityStatus.CorruptZip, sig: "SIG_PACK1", filenameId: "creator.pack.1");
        Assert.Equal(LaneKind.Corrupt, ImportClassifier.Classify(c, Repo));
    }
}
