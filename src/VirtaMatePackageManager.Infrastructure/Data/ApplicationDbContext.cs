using Microsoft.EntityFrameworkCore;
using VirtaMatePackageManager.Core.Entities;

namespace VirtaMatePackageManager.Infrastructure.Data;

/// <summary>
/// Entity Framework Core DbContext for VirtaMatePackageManager.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<InstallationTarget> InstallationTargets => Set<InstallationTarget>();
    public DbSet<VarPackage> VarPackages => Set<VarPackage>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

