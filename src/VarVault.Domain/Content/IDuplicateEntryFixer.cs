using VarVault.Common;

namespace VarVault.Domain.Content;

/// <summary>Result of a successful duplicate-entry repair.</summary>
public sealed record DuplicateEntryFixOutcome(string FixedPath, int EntriesKept, int EntriesDropped);

/// <summary>
/// Rewrites a var that has VaM-colliding zip entry paths (case / separator twins) into a NEW sibling.
/// Default policy is keep-larger per normalized key; optional per-key choices override that.
/// Original is never overwritten.
/// </summary>
public interface IDuplicateEntryFixer
{
    Task<Result<DuplicateEntryFixOutcome>> FixAsync(
        string sourcePath,
        string outputPath,
        IReadOnlyDictionary<string, string>? keepByNormalizedKey = null,
        CancellationToken cancellationToken = default);
}
