using Microsoft.Data.Sqlite;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public class PersistenceTests
{
    [Fact]
    public void Baseline_pragmas_enable_WAL_and_foreign_keys()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"varvault-test-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            SqlitePragmas.ApplyBaseline(connection);

            Assert.Equal("wal", Scalar(connection, "PRAGMA journal_mode;")?.ToString()?.ToLowerInvariant());
            Assert.Equal(1L, Scalar(connection, "PRAGMA foreign_keys;"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileNameWithoutExtension(dbPath) + "*"))
                try { File.Delete(f); } catch (IOException) { }
        }
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
