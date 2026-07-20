using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Import;

/// <summary>The six import lanes (doc 30 §3). Domain-local; Infrastructure maps to <c>Sdk.Import.ImportLane</c>.</summary>
public enum LaneKind { New, Exact, Cjk, Conflict, Naming, Corrupt }

/// <summary>A catalogued var's identity + exact-content signature, for library-wide dedup. (doc 30 §3 · D1.)</summary>
public sealed record CatalogVarFacts(string IdentityKey, string? ContentSignature);

/// <summary>What we learned about an incoming var, reduced to the signals classification needs. (doc 30 §3.)</summary>
public sealed record ImportCandidateFacts(
    IntegrityStatus Integrity,
    string? ContentSignature,
    string? FilenameIdentityKey,   // fold(Creator.Package.Version) from the FILE NAME; null if unparseable
    bool MetaDivergent,            // meta.json's Creator.Package differs from the filename's
    bool HasEncodingIssue);        // valid var, but legacy-CJK entry names (needs Unicode fix)

/// <summary>
/// Classifies an incoming var against the <b>whole library</b> (all repos, D1). Pure. Content-first so a byte-identical
/// copy is skipped no matter how it's named (E1: exact/known content beats the naming warning). Rule 1: a different
/// <em>version</em> is simply New — never compared to another version. (doc 30 §3.)
/// </summary>
public static class ImportClassifier
{
    public static LaneKind Classify(ImportCandidateFacts candidate, IReadOnlyCollection<CatalogVarFacts> catalog)
    {
        Guard.NotNull(candidate);
        Guard.NotNull(catalog);

        // 1 · structurally broken (corrupt zip / missing meta) — nothing else matters.
        if (candidate.Integrity != IntegrityStatus.Ok)
            return LaneKind.Corrupt;

        // 2 · exact content already anywhere in the library → skip (D1; identity-independent, so a misnamed byte-copy
        //     lands here too — E1's "don't import a garbage-named duplicate").
        if (!string.IsNullOrEmpty(candidate.ContentSignature) &&
            catalog.Any(v => SigEq(v.ContentSignature, candidate.ContentSignature)))
            return LaneKind.Exact;

        // 3 · same filename identity present but different content → same version, different insides → Conflict.
        if (candidate.FilenameIdentityKey is not null &&
            catalog.Any(v => string.Equals(v.IdentityKey, candidate.FilenameIdentityKey, StringComparison.Ordinal)))
            return LaneKind.Conflict;

        // 4 · filename can't be trusted (diverges from meta, or unparseable) and it isn't a known dup → Naming warning.
        if (candidate.MetaDivergent || candidate.FilenameIdentityKey is null)
            return LaneKind.Naming;

        // 5 · valid, new content, but legacy-CJK entry names → auto import + Unicode fix.
        if (candidate.HasEncodingIssue)
            return LaneKind.Cjk;

        // 6 · nothing comparable in the library → New (incl. a version you don't have yet).
        return LaneKind.New;
    }

    private static bool SigEq(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.Ordinal);
}
