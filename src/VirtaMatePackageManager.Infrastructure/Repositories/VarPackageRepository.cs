using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;
using VirtaMatePackageManager.Core.Interfaces.Repositories;
using VirtaMatePackageManager.Infrastructure.Data;

namespace VirtaMatePackageManager.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for VAR package operations.
/// </summary>
public class VarPackageRepository : BaseRepository<VarPackage>, IVarPackageRepository
{
    public VarPackageRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<VarPackage?> GetByVarNameAsync(string varName, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .FirstOrDefaultAsync(v => v.VarName == varName, cancellationToken);
    }

    public async Task<bool> ExistsByVarNameAsync(string varName, CancellationToken cancellationToken = default)
    {
        return await DbSet.AnyAsync(v => v.VarName == varName, cancellationToken);
    }

    public async Task<IEnumerable<VarPackage>> GetByRepositoryIdAsync(int repositoryId, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .Where(v => v.RepositoryId == repositoryId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<VarPackage>> GetByCreatorAsync(string creatorName, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .Where(v => v.CreatorName == creatorName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<VarPackage>> GetByCreatorAndPackageAsync(
        string creatorName,
        string packageName,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .Where(v => v.CreatorName == creatorName && v.PackageName == packageName)
            .ToListAsync(cancellationToken);
    }

    public async Task<VarPackage?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .FirstOrDefaultAsync(v => v.FilePath == filePath, cancellationToken);
    }

    public async Task<IEnumerable<VarPackage>> SearchAsync(
        string? creatorName = null,
        string? packageName = null,
        string? version = null,
        string? licenseType = null,
        int? repositoryId = null,
        bool? isInstalled = null,
        CancellationToken cancellationToken = default)
    {
        var query = DbSet.Include(v => v.Repository).AsQueryable();

        if (!string.IsNullOrWhiteSpace(creatorName))
        {
            query = query.Where(v => v.CreatorName.Contains(creatorName));
        }

        if (!string.IsNullOrWhiteSpace(packageName))
        {
            query = query.Where(v => v.PackageName.Contains(packageName));
        }

        if (!string.IsNullOrWhiteSpace(version))
        {
            query = query.Where(v => v.Version == version);
        }

        if (!string.IsNullOrWhiteSpace(licenseType))
        {
            query = query.Where(v => v.LicenseType == licenseType);
        }

        if (repositoryId.HasValue)
        {
            query = query.Where(v => v.RepositoryId == repositoryId.Value);
        }

        if (isInstalled.HasValue)
        {
            query = query.Where(v => v.Installations.Any(i => i.IsEnabled == isInstalled.Value));
        }

        return await query.ToListAsync(cancellationToken);
    }

    public override async Task<VarPackage?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet
            .Include(v => v.Repository)
            .FirstOrDefaultAsync(v => v.Id == id, cancellationToken);
    }
}

