using Microsoft.Data.Sqlite;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>Durability can be raised to synchronous=FULL for destructive transactions, then restored. (5.9.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SynchronousPragmaTests
{
    [Fact]
    public void Full_and_normal_synchronous_round_trip()
    {
        using var dir = new TempDirectory();
        using var connection = new SqliteConnection($"Data Source={dir.File("s.db")}");
        SqlitePragmas.ApplyBaseline(connection);
        Assert.Equal(1L, Scalar(connection, "PRAGMA synchronous;")); // NORMAL == 1

        SqlitePragmas.ApplySynchronousFull(connection);
        Assert.Equal(2L, Scalar(connection, "PRAGMA synchronous;")); // FULL == 2

        SqlitePragmas.RestoreSynchronousNormal(connection);
        Assert.Equal(1L, Scalar(connection, "PRAGMA synchronous;"));
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
