using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Dependency entity operations.
/// </summary>
public class DependencyRepository : BaseRepository<Dependency>, IDependencyRepository
{
    public DependencyRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<IEnumerable<Dependency>> GetByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(d => d.VarPackage)
            .Include(d => d.ResolvedVarPackage)
            .Where(d => d.VarPackageId == varPackageId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Dependency>> GetUnresolvedDependenciesAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(d => d.VarPackage)
            .Where(d => !d.IsResolved)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Dependency>> GetByDependencyNameAsync(string dependencyName, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(d => d.VarPackage)
            .Include(d => d.ResolvedVarPackage)
            .Where(d => d.DependencyName == dependencyName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Dependency>> GetUnresolvedByVarPackageIdAsync(int varPackageId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(d => d.VarPackage)
            .Where(d => d.VarPackageId == varPackageId && !d.IsResolved)
            .ToListAsync(cancellationToken);
    }
}

