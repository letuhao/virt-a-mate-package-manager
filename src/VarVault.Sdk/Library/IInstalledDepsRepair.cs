namespace VarVault.Sdk.Library;

/// <summary>How an installed-set dependency ref matched the library (legacy Installed Packages / MissingDepends).</summary>
public enum InstalledDepsResolveVia
{
    None = 0,
    Exact = 1,
    Latest = 2,
    Closest = 3,
    Alias = 4,
}

/// <summary>One dependency of the currently installed (active) set, resolved against the library.</summary>
public sealed record InstalledDepsEntry(
    string Ref,
    bool InLibrary,
    string? ResolvedVarName,
    InstalledDepsResolveVia Via,
    int NeededByCount,
    /// <summary>True when the user should save an alias (truly missing, or closest-version substitute).</summary>
    bool NeedsAlias);

/// <summary>Result of analysing dependencies of active/installed packages.</summary>
public sealed record InstalledDepsAnalysis(
    IReadOnlyList<InstalledDepsEntry> Entries,
    int ActivePackageCount)
{
    public int Parsed => Entries.Count;
    public int InLibrary => Entries.Count(e => e.InLibrary);
    public int NotInLibrary => Entries.Count(e => !e.InLibrary);
    public int Closest => Entries.Count(e => e.Via == InstalledDepsResolveVia.Closest);
    public IReadOnlyList<InstalledDepsEntry> Leftovers =>
        Entries.Where(e => e.NeedsAlias).ToList();
}

/// <summary>
/// Legacy <c>MissingDepends</c> / “Installed Packages”: analyse deps of packages active on the current
/// VaM profile, auto-activate ones present in the library (incl. closest-version substitutes), and surface
/// leftovers for alias Resolve. (varManager Form1.MissingDepends.)
/// </summary>
public interface IInstalledDepsRepair
{
    /// <summary>Collect distinct deps of <c>PackageListItem.IsActive</c> packages and resolve each against the catalog.</summary>
    Task<InstalledDepsAnalysis> AnalyzeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Add the given in-library var names to the active loading preset and rebuild profile links
    /// (same path as <see cref="IMissingLogResolver.ActivateAsync"/>).
    /// </summary>
    Task<MissingLogActivation> ActivateFoundAsync(
        IReadOnlyList<string> varNames,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate packages from an analysis that aren't already active; rebuild profile links when only
    /// aliases need materializing under <c>___MissingVarLink___</c>.
    /// </summary>
    Task<MissingLogActivation> ActivateFromAnalysisAsync(
        InstalledDepsAnalysis analysis,
        CancellationToken cancellationToken = default);
}
