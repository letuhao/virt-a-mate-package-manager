using System.IO;

namespace VarVault.TestKit;

/// <summary>
/// Resolves real-var test-corpus roots from <b>environment variables</b> — never hardcoded machine paths, so a
/// fresh clone builds and runs green (real-data tests early-return when the vars are unset, instead of failing or
/// referencing a drive the cloner doesn't have). Point the vars at real repositories to exercise the real-data /
/// real-app E2E paths:
/// <list type="bullet">
///   <item><c>VARVAULT_TEST_CORPUS</c>   — primary repo root (a folder tree of <c>.var</c> files).</item>
///   <item><c>VARVAULT_TEST_CORPUS_2</c> — a second repo, ideally on another physical drive (cross-drive / multi-repo).</item>
///   <item><c>VARVAULT_TEST_CORPUS_3/4</c> — optional further repos for multi-repo theories.</item>
/// </list>
/// (24-checklist D1/D2 — replaces the old hardcoded <c>D:\VarVault_test_repo</c> literals. doc 26 · G-9 —
/// real-data tests now use <c>[SkippableFact]</c> + <c>Skip.If(TestCorpus.Primary is null, …)</c> so a missing
/// corpus reports as <b>SKIPPED</b>, not silently green.)
/// </summary>
public static class TestCorpus
{
    public const string PrimaryVar = "VARVAULT_TEST_CORPUS";
    public const string SecondaryVar = "VARVAULT_TEST_CORPUS_2";

    /// <summary>Primary corpus root if its env var is set and the folder exists; otherwise null.</summary>
    public static string? Primary => Resolve(PrimaryVar);

    /// <summary>Second corpus root (cross-drive tests) if set and present; otherwise null.</summary>
    public static string? Secondary => Resolve(SecondaryVar);

    private static string? Resolve(string envVar)
    {
        var p = Environment.GetEnvironmentVariable(envVar);
        return !string.IsNullOrWhiteSpace(p) && Directory.Exists(p) ? p : null;
    }

    /// <summary>
    /// All configured roots that exist (primary…4) as <c>[MemberData]</c> rows for real-repo theories. Always yields
    /// at least one row — a sentinel empty string when nothing is configured — so the theory reports one (early-return)
    /// case rather than an xUnit "no data" failure. The test body must return on an empty path.
    /// </summary>
    public static IEnumerable<object[]> ConfiguredRoots()
    {
        var any = false;
        foreach (var v in new[] { PrimaryVar, SecondaryVar, "VARVAULT_TEST_CORPUS_3", "VARVAULT_TEST_CORPUS_4" })
        {
            if (Resolve(v) is { } r)
            {
                any = true;
                yield return [r];
            }
        }
        if (!any)
            yield return [""]; // sentinel → test returns early
    }

    /// <summary>(primary, secondary) as a single <c>[MemberData]</c> row for cross-drive tests; sentinel when either is unset.</summary>
    public static IEnumerable<object[]> ConfiguredRootPairs()
    {
        if (Primary is { } a && Secondary is { } b)
            yield return [a, b];
        else
            yield return ["", ""]; // sentinel → test returns early
    }
}
