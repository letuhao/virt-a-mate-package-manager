using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Installation entity operations.
/// </summary>
public class InstallationRepository : BaseRepository<Installation>, IInstallationRepository
{
    public InstallationRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<IEnumerable<Installation>> GetByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(i => i.VarPackage)
            .Include(i => i.InstallationTarget)
            .Where(i => i.VarPackageId == varPackageId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Installation>> GetByTargetIdAsync(int installationTargetId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(i => i.VarPackage)
            .Include(i => i.InstallationTarget)
            .Where(i => i.InstallationTargetId == installationTargetId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Installation?> GetInstallationAsync(int varPackageId, int installationTargetId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(i => i.VarPackage)
            .Include(i => i.InstallationTarget)
            .FirstOrDefaultAsync(i => i.VarPackageId == varPackageId && i.InstallationTargetId == installationTargetId, cancellationToken);
    }

    public async Task<IEnumerable<Installation>> GetEnabledInstallationsAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(i => i.VarPackage)
            .Include(i => i.InstallationTarget)
            .Where(i => i.IsEnabled)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Installation>> GetEnabledByTargetIdAsync(int installationTargetId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(i => i.VarPackage)
            .Include(i => i.InstallationTarget)
            .Where(i => i.InstallationTargetId == installationTargetId && i.IsEnabled)
            .ToListAsync(cancellationToken);
    }
}

