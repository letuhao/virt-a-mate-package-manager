using Microsoft.EntityFrameworkCore;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// The catalog database context. Entities and their <c>IEntityTypeConfiguration</c>s are
/// added in Slice 1 and auto-applied from this assembly.
/// </summary>
public class VarVaultDbContext(DbContextOptions<VarVaultDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VarVaultDbContext).Assembly);
    }
}
