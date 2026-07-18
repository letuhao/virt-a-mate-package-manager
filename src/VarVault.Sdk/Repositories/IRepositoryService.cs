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
}
