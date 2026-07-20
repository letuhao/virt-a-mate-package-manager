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
        // Pooling=False: each connection fully closes on context dispose, so the temp db file is unlocked
        // for cleanup without a process-global SqliteConnection.ClearAllPools() — that global call, fired
        // per-instance while xUnit runs test classes in parallel, disposed native handles other tests were
        // mid-migration on (intermittent ObjectDisposedException). (24-checklist D3.)
        var options = new DbContextOptionsBuilder<VarVaultDbContext>()
            .UseSqlite($"Data Source={Path};Pooling=False")
            .Options;

        var db = new VarVaultDbContext(options);
        db.Database.Migrate(); // applies migrations incl. the FTS5 virtual table
        SqlitePragmas.ApplyBaseline(db.Database.GetDbConnection());
        return db;
    }

    public void Dispose() => _dir.Dispose();
}
