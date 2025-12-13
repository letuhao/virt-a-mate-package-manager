using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for InstallationTarget entity operations.
/// </summary>
public class InstallationTargetRepository : BaseRepository<InstallationTarget>, IInstallationTargetRepository
{
    public InstallationTargetRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<IEnumerable<InstallationTarget>> GetActiveTargetsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<InstallationTarget?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return await DbSet
            .FirstOrDefaultAsync(t => t.Path == normalizedPath, cancellationToken);
    }

    public async Task<InstallationTarget?> GetDefaultTargetAsync(CancellationToken cancellationToken = default)
    {
        // Get the first active target as default
        return await DbSet
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

