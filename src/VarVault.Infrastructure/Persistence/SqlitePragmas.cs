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

    public static void ApplyBaseline(DbConnection connection) => Run(connection, Baseline);

    /// <summary>
    /// Raise durability to <c>synchronous=FULL</c> for a transaction that gates a destructive filesystem
    /// op (migration/delete), so a power loss can't leave the DB out of sync with disk. (Checklist 5.9.)
    /// </summary>
    public static void ApplySynchronousFull(DbConnection connection) => Run(connection, "PRAGMA synchronous=FULL;");

    /// <summary>Return to the baseline <c>synchronous=NORMAL</c> after the destructive op completes.</summary>
    public static void RestoreSynchronousNormal(DbConnection connection) => Run(connection, "PRAGMA synchronous=NORMAL;");

    private static void Run(DbConnection connection, string sql)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.State != ConnectionState.Open)
            connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
