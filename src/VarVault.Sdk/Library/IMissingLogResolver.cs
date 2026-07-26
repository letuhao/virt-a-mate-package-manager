namespace VarVault.Sdk.Library;

/// <summary>One package a VaM log asked for, resolved against the library. (QoL log-repair.)</summary>
public sealed record MissingLogEntry(
    string Ref,               // the requested ref as parsed, e.g. "Creator.Package.latest" or "Creator.Package.7"
    string Creator,
    string Package,
    bool IsLatest,            // the ref asked for ".latest"
    string? ResolvedVarName,  // the concrete var we'd use (latest resolved), null when not in the library
    bool InLibrary,           // true → we have it and can activate it
    int? Tier,
    string? RepositoryName);

/// <summary>The result of analysing a pasted VaM log against the library. (QoL log-repair.)</summary>
public sealed record MissingLogAnalysis(IReadOnlyList<MissingLogEntry> Entries)
{
    public int Parsed => Entries.Count;
    public int InLibrary => Entries.Count(e => e.InLibrary);
    public int NotInLibrary => Entries.Count(e => !e.InLibrary);
}

/// <summary>Outcome of activating the in-library subset (members + their dependency closure). (QoL log-repair.)</summary>
public sealed record MissingLogActivation(
    int MembersActivated,     // explicit packages requested that were linked
    int LinksCreated,         // total links made this build (members + pulled-in dependency closure)
    int StillMissing,         // closure packages with no online copy to link (need import)
    int PrivilegeFailures,    // >0 → symlink privilege denied (needs Developer Mode / admin)
    int UnresolvedDependencies = 0, // unresolved members + transitive refs that stopped a branch
    int PathUnavailable = 0); // 1 when VaM path unset/missing

/// <summary>
/// Turns a pasted VaM error log into an actionable repair: parse the missing <c>Creator.Package.version</c> refs
/// (robust — see <c>VamLogParser</c>), resolve <c>.latest</c> to the highest <c>VersionSort</c> already in the
/// library, then add the found set <b>plus its dependency closure</b> into the <b>active</b> loading preset /
/// VaM profile (reusing the preset/activation flow — the fix for the old importer that never pulled
/// dependencies). (QoL log-repair.)
/// </summary>
public interface IMissingLogResolver
{
    Task<MissingLogAnalysis> AnalyzeAsync(string logText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given (in-library) var names to the active loading preset, then rebuild its profile links
    /// (members + forward-dependency closure). Does not create a throwaway repair preset.
    /// </summary>
    Task<MissingLogActivation> ActivateAsync(IReadOnlyList<string> varNames, CancellationToken cancellationToken = default);
}
