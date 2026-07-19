using VarVault.Common;
using VarVault.Domain.Identity;

namespace VarVault.Domain.Dedup;

/// <summary>Signatures + identity of a var, for intake comparison against the catalog.</summary>
public sealed record VarSignatureFacts(
    string IdentityKey,
    string? ContentSignature,
    string? PayloadSignature,
    string? ContentSignatureNoPath);

/// <summary>How an incoming download relates to what's already in the catalog. (Checklist 4.7.)</summary>
public enum IntakeClass
{
    /// <summary>Same identity, byte-identical content — already have it.</summary>
    ExactDuplicate = 0,
    /// <summary>Same identity but different content — a content conflict (same name, different insides).</summary>
    SameNameDifferentContent = 1,
    /// <summary>Different identity, same payload (content minus meta) — a near-duplicate. (4.6)</summary>
    NearDuplicate = 2,
    /// <summary>An encoding-fixed twin of an existing var (paths differ, size+CRC match).</summary>
    EncodingVariant = 3,
    /// <summary>Nothing comparable in the catalog.</summary>
    New = 4,
}

/// <summary>
/// Classifies an incoming var against the catalog: exact duplicate / same-name-different-content /
/// near-duplicate / encoding variant / new. Pure. (Checklist 4.6/4.7.)
/// </summary>
public static class IntakeClassifier
{
    public static IntakeClass Classify(VarSignatureFacts candidate, IEnumerable<VarSignatureFacts> catalog)
    {
        Guard.NotNull(candidate);
        Guard.NotNull(catalog);

        var sawSameIdentity = false;
        var sawNearDup = false;
        var sawEncodingVariant = false;

        foreach (var existing in catalog)
        {
            var sameIdentity = string.Equals(existing.IdentityKey, candidate.IdentityKey, StringComparison.Ordinal);

            if (sameIdentity)
            {
                if (SignaturesMatch(existing.ContentSignature, candidate.ContentSignature))
                    return IntakeClass.ExactDuplicate; // strongest match wins immediately
                sawSameIdentity = true;
            }
            else
            {
                if (SignaturesMatch(existing.PayloadSignature, candidate.PayloadSignature))
                    sawNearDup = true;
                else if (SignaturesMatch(existing.ContentSignatureNoPath, candidate.ContentSignatureNoPath))
                    sawEncodingVariant = true;
            }
        }

        if (sawSameIdentity)
            return IntakeClass.SameNameDifferentContent;
        if (sawNearDup)
            return IntakeClass.NearDuplicate;
        if (sawEncodingVariant)
            return IntakeClass.EncodingVariant;
        return IntakeClass.New;
    }

    private static bool SignaturesMatch(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>Convenience: fold a var name to the identity key used in <see cref="VarSignatureFacts"/>.</summary>
    public static string IdentityKeyFor(string varName) => IdentityFold.Compute(varName);
}
