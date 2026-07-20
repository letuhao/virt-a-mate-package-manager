using VarVault.Domain.Entities;
using VarVault.Domain.ValueObjects;

namespace VarVault.Domain.Indexing;

/// <summary>A content item to persist for a var (previewable types; assets excluded upstream).</summary>
public sealed record UpsertContentItem(
    ContentType Type,
    string EntryPath,
    bool IsPreset,
    Gender? Gender,
    double? GenderConfidence);

/// <summary>
/// Everything the catalog needs to persist one var, precomputed by the indexing orchestrator. The
/// store maps this to <see cref="Package"/> / <see cref="VarFile"/> / <see cref="ContentItem"/> /
/// <see cref="Dependency"/> / <see cref="PackageContentCount"/> rows. A null <see cref="Identity"/>
/// means the filename didn't parse (Unrecognized bucket → <c>VarFile.PackageId = null</c>).
/// </summary>
public sealed record VarUpsert(
    Guid RepositoryId,
    string RelativePath,
    long SizeBytes,
    DateTime FileMtimeUtc,
    QuarantineKind Quarantine,
    PackageId? Identity,
    IntegrityStatus Integrity,
    // meta.json facts
    string? MetaCreator,
    string? MetaPackage,
    string? LicenseType,
    string? Description,
    string? ProgramVersion,
    // fingerprints
    string? ContentSignature,
    string? PayloadSignature,
    string? ContentSignatureNoPath,
    // encoding health
    EncodingHealth EncodingHealth,
    string? DetectedCodepage,
    int BrokenEntryCount,
    // content
    IReadOnlyList<UpsertContentItem> ContentItems,
    IReadOnlyDictionary<ContentType, int> ContentCounts,
    // dependency ref strings (raw, from meta.json)
    IReadOnlyList<string> DependencyRefsRaw,
    // dependency ref strings harvested from embedded scene/preset JSON (2.1)
    IReadOnlyList<string> EmbeddedRefsRaw)
{
    /// <summary>True when meta.json's author names diverge from the filename identity (folded compare).</summary>
    public bool MetaDivergent =>
        Identity is not null &&
        ((MetaCreator is not null && !Identity.MatchesCreator(MetaCreator)) ||
         (MetaPackage is not null && !Identity.MatchesPackage(MetaPackage)));
}

/// <summary>A VarFile row as it currently exists on disk-record, for freshness/prune diffing.</summary>
public sealed record ExistingVarFile(long Id, string RelativePath, long SizeBytes, DateTime FileMtimeUtc, long? PackageId);

/// <summary>Aggregate counts from one indexing run.</summary>
public sealed record IndexOutcome(int Indexed, int Skipped, int Pruned, int Corrupt, int Unrecognized);

/// <summary>
/// The persistence seam for indexing. Defined in Domain (operates on domain entities), implemented by
/// Infrastructure over EF, consumed by the indexing module — so the module never binds to EF. All
/// mutating calls run inside the single-writer queue. (Data-arch §5.4, architecture doc 11.)
/// </summary>
public interface ICatalogStore
{
    /// <summary>Whether a repository row exists and is online (drives prune-vs-keep on vanished files).</summary>
    Task<bool> RepositoryIsOnlineAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>Existing VarFile records for a repository, keyed for freshness/prune diffing.</summary>
    Task<IReadOnlyList<ExistingVarFile>> ListVarFilesAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>Upsert one var (package + varfile + content items + deps + counts) and return its Package id (if any).</summary>
    Task<long?> ApplyAsync(VarUpsert upsert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upsert a batch of vars in ONE transaction (bounded change tracker). The scalable path for a full index —
    /// use this instead of many <see cref="ApplyAsync"/> calls. Returns each var's Package id, in order.
    /// </summary>
    Task<IReadOnlyList<long?>> ApplyBatchAsync(IReadOnlyList<VarUpsert> upserts, CancellationToken cancellationToken = default);

    /// <summary>Remove VarFiles that vanished from an online repository (re-electing canonicals as needed).</summary>
    Task<int> RemoveVarFilesAsync(IReadOnlyCollection<long> varFileIds, CancellationToken cancellationToken = default);

    /// <summary>Refresh the materialized <see cref="PackageListItem"/> rows for the given packages. (0.26/1.36.)</summary>
    Task RefreshReadModelAsync(IReadOnlyCollection<long> packageIds, CancellationToken cancellationToken = default);
}
