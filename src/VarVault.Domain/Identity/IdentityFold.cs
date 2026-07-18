using System.Text;
using VarVault.Common;

namespace VarVault.Domain.Identity;

/// <summary>
/// Computes the <c>IdentityKey</c> used for all identity matching (packages, dependency
/// refs, preset members, aliases, save deps). The library is heavily non-ASCII (CJK), and
/// SQLite's <c>NOCASE</c> is ASCII-only, so the fold key is computed in the app:
/// <b>NFC normalization + full-culture-invariant case fold</b>.
/// <para>
/// Properties guaranteed: strings differing only by case fold to the same key
/// (<c>Fold("Café") == Fold("café")</c>, <c>Fold("МЕ") == Fold("ме")</c>); CJK is caseless
/// so it is preserved; the operation is idempotent (<c>Fold(Fold(x)) == Fold(x)</c>).
/// </para>
/// <para>
/// Case folding uses <see cref="string.ToUpperInvariant"/>, which Microsoft recommends over
/// <c>ToLowerInvariant</c> for case-insensitive keys because a few lowercase code points do
/// not round-trip. NFC is applied before and after folding so the result is canonical
/// regardless of how the input was composed.
/// </para>
/// </summary>
public static class IdentityFold
{
    /// <summary>
    /// Compute the fold key for an identity string (e.g. a var name or a dependency ref).
    /// Returns <see cref="string.Empty"/> for empty input; never returns null.
    /// </summary>
    public static string Compute(string identity)
    {
        Guard.NotNull(identity);
        if (identity.Length == 0)
            return string.Empty;

        var nfc = identity.Normalize(NormalizationForm.FormC);
        var folded = nfc.ToUpperInvariant();
        // Fold again to NFC: ToUpperInvariant can denormalize some code points.
        return folded.Normalize(NormalizationForm.FormC);
    }

    /// <summary>True if the two identity strings match after folding.</summary>
    public static bool Equal(string a, string b) =>
        string.Equals(Compute(a), Compute(b), StringComparison.Ordinal);
}
