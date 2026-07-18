using VarVault.Domain.Entities;

namespace VarVault.Domain.Indexing;

/// <summary>
/// A <c>.var</c> discovered by a repository scan, described from its directory entry alone (no file
/// open). <see cref="Quarantine"/> is <see cref="QuarantineKind.None"/> for a live var, else the
/// legacy bucket it sits in. (IDX-1, checklist 1.11/1.29.)
/// </summary>
public sealed record ScannedVar(
    string FullPath,
    string RelativePath,
    long SizeBytes,
    DateTime FileMtimeUtc,
    QuarantineKind Quarantine)
{
    public bool IsLive => Quarantine == QuarantineKind.None;
}
