using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context at design time (migrations) without booting the host.
/// Uses a throwaway local file path; never used at runtime.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<VarVaultDbContext>
{
    public VarVaultDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<VarVaultDbContext>()
            .UseSqlite("Data Source=varvault-designtime.db")
            .Options;
        return new VarVaultDbContext(options);
    }
}
