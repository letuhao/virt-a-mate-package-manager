using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VirtaMatePackageManager.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating DbContext instances during migrations.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Default connection string for design-time (migrations)
        // Can be overridden via environment variable or user secrets
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
            ?? "Host=localhost;Port=5432;Database=virtamate_package_manager;Username=postgres;Password=postgres";
        
        optionsBuilder.UseNpgsql(connectionString);
        
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}

