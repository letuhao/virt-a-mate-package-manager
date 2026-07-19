using VarVault.Common;

namespace VarVault.Sdk.Repositories;

/// <summary>Request to register a repository folder.</summary>
public sealed record RegisterRepositoryRequest(string Name, string Path);

/// <summary>A repository as seen across module boundaries (SDK-safe; no EF entities). </summary>
public sealed record RepositoryInfo(
    Guid Id,
    string Name,
    string MountPath,
    string MediaType,
    int Tier,
    bool IsOnline,
    bool IsEnabled,
    long? CapacityBytes,
    long? FreeBytes,
    string? VolumeSerial);

/// <summary>
/// Registers and manages storage repositories: profiles the drive (media/capacity/serial), assigns a
/// tier, and rejects unsafe paths (AddonPackages / overlap). The public boundary for the UI/CLI.
/// (Pillar 1; checklist 1.1–1.10.)
/// </summary>
public interface IRepositoryService
{
    Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken cancellationToken = default);
    Task<bool> SetEnabledAsync(Guid repositoryId, bool enabled, CancellationToken cancellationToken = default);
    Task<RepositoryInfo?> RefreshCapacityAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-point a repository to a new path (drive-letter shuffle / portable catalog). Blocked if the new
    /// volume's serial doesn't match the captured one — the drive is different, so vars aren't there.
    /// (Checklist 1.4/BE-R4, X.10.)
    /// </summary>
    Task<Result<RepositoryInfo>> RepointAsync(Guid repositoryId, string newPath, CancellationToken cancellationToken = default);

    /// <summary>Benchmark the repo's drive, store the speeds, and re-assign its tier from them. (BE-R1/1.5/1.6.)</summary>
    Task<RepositoryInfo?> BenchmarkAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>Manually override a repository's tier (user picks T1/T2/T3 from the card). (BE-G2)</summary>
    Task<RepositoryInfo?> SetTierAsync(Guid repositoryId, int tier, CancellationToken cancellationToken = default);
}
