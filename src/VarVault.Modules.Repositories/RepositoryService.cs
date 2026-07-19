using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;
using VarVault.Sdk.Repositories;

namespace VarVault.Modules.Repositories;

/// <summary>
/// Registers and manages repositories: validates the path (not AddonPackages / not overlapping),
/// profiles the drive (media/capacity/serial), assigns a tier, and persists — all through Domain/SDK
/// seams. (Pillar 1; checklist 1.1/1.2/1.3/1.7/1.9, BE-R2/R3/R4/R5.)
/// </summary>
internal sealed class RepositoryService(
    IServiceScopeFactory scopeFactory,
    IDriveProfiler driveProfiler,
    IClock clock,
    ILogger<RepositoryService> logger) : IRepositoryService
{
    private readonly TierPolicy _tierPolicy = TierPolicy.Default;

    public async Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest request, CancellationToken cancellationToken = default)
    {
        Guard.NotNull(request);
        if (!System.IO.Directory.Exists(request.Path))
            return Result.Failure<RepositoryInfo>("repo.path.missing", $"Folder does not exist: {request.Path}");

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();

        var existing = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        var validation = RepositoryPathValidator.Validate(
            request.Path, addonPackagesPath: null, existing.Select(r => r.MountPath));
        if (validation.IsFailure)
            return Result.Failure<RepositoryInfo>(validation.Error);

        var profile = driveProfiler.Profile(request.Path);
        var now = clock.UtcNow.UtcDateTime;
        var repo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            MountPath = System.IO.Path.GetFullPath(request.Path),
            MediaType = profile.MediaType,
            // No benchmark yet (BE-R1) → tier from media type; re-tier after a benchmark later.
            Tier = _tierPolicy.AssignTier(profile.MediaType, readMBps: null),
            CapacityBytes = profile.CapacityBytes,
            FreeBytes = profile.FreeBytes,
            VolumeSerial = profile.VolumeSerial,
            IsEnabled = true,
            IsOnline = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await store.AddAsync(repo, cancellationToken).ConfigureAwait(false);
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Registered repository {Name} at {Path} ({Media}, tier {Tier})",
            repo.Name, repo.MountPath, repo.MediaType, repo.Tier);

        return Map(repo);
    }

    public async Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var repos = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        return repos.Select(Map).ToList();
    }

    public async Task<bool> SetEnabledAsync(Guid repositoryId, bool enabled, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var repo = await store.FindAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return false;
        repo.IsEnabled = enabled;
        repo.UpdatedAt = clock.UtcNow.UtcDateTime;
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<RepositoryInfo?> RefreshCapacityAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var repo = await store.FindAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return null;

        var (capacity, free) = driveProfiler.GetCapacity(repo.MountPath);
        repo.CapacityBytes = capacity;
        repo.FreeBytes = free;
        repo.UpdatedAt = clock.UtcNow.UtcDateTime;
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(repo);
    }

    public async Task<Result<RepositoryInfo>> RepointAsync(Guid repositoryId, string newPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(newPath);
        if (!System.IO.Directory.Exists(newPath))
            return Result.Failure<RepositoryInfo>("repo.path.missing", $"Folder does not exist: {newPath}");

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRepositoryStore>();
        var repo = await store.FindAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repo is null)
            return Result.Failure<RepositoryInfo>("repo.missing", "Repository not found.");

        var profile = driveProfiler.Profile(newPath);

        // ⚠ Strict serial match: if we captured a serial and the new volume's differs, it's a different
        // drive — refuse and mark read-only rather than silently mis-binding. (1.4/BE-R4)
        if (!string.IsNullOrEmpty(repo.VolumeSerial) &&
            !string.IsNullOrEmpty(profile.VolumeSerial) &&
            !string.Equals(repo.VolumeSerial, profile.VolumeSerial, StringComparison.OrdinalIgnoreCase))
        {
            repo.IsReadOnly = true;
            repo.UpdatedAt = clock.UtcNow.UtcDateTime;
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Failure<RepositoryInfo>(
                "repo.repoint.serial",
                $"The volume at '{newPath}' has a different serial than this repository — refusing to re-point (marked read-only).");
        }

        repo.MountPath = System.IO.Path.GetFullPath(newPath);
        repo.VolumeSerial ??= profile.VolumeSerial;
        repo.CapacityBytes = profile.CapacityBytes;
        repo.FreeBytes = profile.FreeBytes;
        repo.IsOnline = true;
        repo.IsReadOnly = false;
        repo.UpdatedAt = clock.UtcNow.UtcDateTime;
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Re-pointed repository {Id} to {Path}", repo.Id, repo.MountPath);
        return Map(repo);
    }

    private static RepositoryInfo Map(Repository r) => new(
        r.Id, r.Name, r.MountPath, r.MediaType.ToString(), r.Tier,
        r.IsOnline, r.IsEnabled, r.CapacityBytes, r.FreeBytes, r.VolumeSerial);
}
