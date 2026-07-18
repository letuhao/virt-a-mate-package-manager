using VarVault.Common;
using VarVault.Domain.Entities;

namespace VarVault.Domain.Content;

/// <summary>Inferred gender plus a confidence in [0,1]. Fragile heuristic — never authoritative. (BE-C4.)</summary>
public sealed record GenderGuess(Gender Gender, double Confidence);

/// <summary>
/// Infers a content item's target gender from path fragments (<c>female</c>/<c>male</c>/<c>futa</c>),
/// voting across entries with a confidence = winning votes ÷ total gendered votes. No gendered
/// fragment → <see cref="Gender.Auto"/> at zero confidence. (BE-C4.)
/// </summary>
public static class GenderInference
{
    public static GenderGuess Infer(IEnumerable<string> entryPaths)
    {
        Guard.NotNull(entryPaths);

        var votes = new Dictionary<Gender, int>();
        var total = 0;

        foreach (var raw in entryPaths)
        {
            var path = raw.Replace('\\', '/').ToLowerInvariant();
            // futa before female/male because a futa path often also contains "female".
            Gender? g = null;
            if (path.Contains("futa", StringComparison.Ordinal)) g = Gender.Futa;
            else if (ContainsToken(path, "female")) g = Gender.Female;
            else if (ContainsToken(path, "male")) g = Gender.Male;

            if (g is { } gender)
            {
                votes[gender] = votes.GetValueOrDefault(gender) + 1;
                total++;
            }
        }

        if (total == 0)
            return new GenderGuess(Gender.Auto, 0);

        var winner = Gender.Auto;
        var best = 0;
        foreach (var (gender, count) in votes)
        {
            if (count > best)
            {
                best = count;
                winner = gender;
            }
        }

        return new GenderGuess(winner, (double)best / total);
    }

    // "male" must not match inside "female"; require a non-letter boundary before the token.
    private static bool ContainsToken(string path, string token)
    {
        var idx = 0;
        while ((idx = path.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
        {
            var before = idx == 0 ? '/' : path[idx - 1];
            if (!char.IsAsciiLetter(before))
                return true;
            idx += token.Length;
        }
        return false;
    }
}
