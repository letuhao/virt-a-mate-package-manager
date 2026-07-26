namespace VarVault.Sdk.Library;

/// <summary>Preview of matching VaM log tokens to library packages for usage import.</summary>
public sealed record VamLogUsagePreview(
    int Parsed,
    int MatchedInLibrary,
    int Unmatched,
    IReadOnlyList<string> SampleMatchedVarNames);

/// <summary>Result of writing Load + VamLogImport usage events from a VaM log.</summary>
public sealed record VamLogUsageImportResult(
    int EventsRecorded,
    int PackagesRecomputed,
    int SkippedNotInLibrary);

/// <summary>
/// Seeds tier scoring from a VaM <c>output_log.txt</c> without overloading Missing-deps Analyze.
/// Records <c>UsageKind.Load</c> / <c>UsageSource.VamLogImport</c> for in-library matches only.
/// </summary>
public interface IVamLogUsageImporter
{
    Task<VamLogUsagePreview> PreviewAsync(string logText, CancellationToken cancellationToken = default);
    Task<VamLogUsageImportResult> ImportAsync(string logText, CancellationToken cancellationToken = default);
}
