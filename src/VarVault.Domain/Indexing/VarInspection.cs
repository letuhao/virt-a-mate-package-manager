using VarVault.Common;
using VarVault.Domain.Content;
using VarVault.Domain.Entities;
using VarVault.Domain.Fingerprinting;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Everything learned by opening one var: integrity, entries, the three fingerprints, content
/// classification, encoding health, and meta. A structurally-corrupt zip yields
/// <see cref="IntegrityStatus.CorruptZip"/> with the derived facts null. (IDX-1/5/6/8/10.)
/// </summary>
public sealed record VarInspection(
    IntegrityStatus Integrity,
    IReadOnlyList<ZipEntryFacts> Entries,
    VarSignatures? Signatures,
    VarContentClassification? Classification,
    EncodingHealthResult? Encoding,
    VarMeta? Meta)
{
    public bool HasMeta => Meta is not null;
}

/// <summary>Opens and fully inspects a var file. Implemented by Infrastructure (zip I/O). (IDX-1/5/6/8.)</summary>
public interface IVarInspector
{
    /// <summary>
    /// Inspect the var at <paramref name="varPath"/>. Returns a failure only for I/O errors
    /// (missing/locked file); a structurally-corrupt archive is reported via the inspection's
    /// <see cref="VarInspection.Integrity"/>, not as a failure.
    /// </summary>
    Result<VarInspection> Inspect(string varPath, CancellationToken cancellationToken = default);
}
