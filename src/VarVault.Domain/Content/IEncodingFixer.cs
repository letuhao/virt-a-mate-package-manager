using VarVault.Common;

namespace VarVault.Domain.Content;

/// <summary>Result of a successful encoding fix.</summary>
public sealed record EncodingFixOutcome(string FixedPath, int EntriesRewritten);

/// <summary>
/// 🔒⚠ Repairs a broken-encoding var by writing a NEW UTF-8 var (never overwriting in place):
/// temp-write → validate against VaM's constraints → atomic rename; the original is always retained.
/// Implemented by Infrastructure (zip I/O). (IDX-9; checklist 4.12/4.13/4.14.)
/// </summary>
public interface IEncodingFixer
{
    /// <summary>
    /// Re-encode <paramref name="sourcePath"/>'s entry names from <paramref name="codePage"/> to UTF-8,
    /// writing the fixed var to <paramref name="outputPath"/>. Returns a failure (original untouched) if
    /// the rewrite doesn't validate.
    /// </summary>
    Task<Result<EncodingFixOutcome>> FixAsync(
        string sourcePath,
        int codePage,
        string outputPath,
        CancellationToken cancellationToken = default);
}
