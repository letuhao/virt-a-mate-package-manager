using System.Data;
using System.Data.Common;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Applies the standard connection PRAGMAs. WAL + NORMAL sync + FK enforcement is the
/// baseline; <c>synchronous=FULL</c> is applied per-transaction for destructive filesystem
/// operations (migration/delete), not globally. (Data-architecture §6, §5.6.)
/// </summary>
public static class SqlitePragmas
{
    public const string Baseline =
        "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";

    public static void ApplyBaseline(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Baseline;
        command.ExecuteNonQuery();
    }
}
