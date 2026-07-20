namespace VarVault.Sdk.Library;

/// <summary>One incoming var classified against the catalog: how it relates + what to do. (24-checklist A10.)</summary>
public sealed record IntakeItem(string FileName, string Classification, string SuggestedAction);

/// <summary>
/// BE · Download-intake. Classifies each incoming <c>.var</c> in a folder against the catalog
/// (exact-dup / same-name-different-content / near-dup / encoding-variant / new) via the domain
/// <c>IntakeClassifier</c>, so ambiguous downloads are auto-sorted rather than dumped for manual review.
/// (Doc 02 line 96 · 24-checklist A10.)
/// </summary>
public interface IIntakeService
{
    Task<IReadOnlyList<IntakeItem>> ClassifyFolderAsync(string folder, CancellationToken cancellationToken = default);
}
