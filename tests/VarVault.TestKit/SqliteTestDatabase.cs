using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VarVault.Infrastructure.Persistence;

namespace VarVault.TestKit;

/// <summary>
/// A real on-disk SQLite catalog DB for integration tests, with baseline pragmas applied.
/// Fresh temp file per instance; deletes on dispose.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly TempDirectory _dir = new();
    public string Path { get; }

    public SqliteTestDatabase() => Path = _dir.File("catalog.db");

    public VarVaultDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<VarVaultDbContext>()
            .UseSqlite($"Data Source={Path}")
            .Options;

        var db = new VarVaultDbContext(options);
        db.Database.EnsureCreated();
        SqlitePragmas.ApplyBaseline(db.Database.GetDbConnection());
        return db;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }
}
