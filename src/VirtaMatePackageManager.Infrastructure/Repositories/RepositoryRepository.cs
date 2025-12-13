using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Repository entity operations.
/// </summary>
public class RepositoryRepository : BaseRepository<Repository>, IRepositoryRepository
{
    public RepositoryRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<IEnumerable<Repository>> GetEnabledRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Where(r => r.Enabled)
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Repository>> GetByPriorityAsync(CancellationToken cancellationToken = default)
    {
        return await DbSet
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<Repository?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return await DbSet
            .FirstOrDefaultAsync(r => r.Path == normalizedPath, cancellationToken);
    }
}

