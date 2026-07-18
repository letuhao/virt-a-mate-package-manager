using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using VarVault.Common;

namespace VarVault.Infrastructure.Persistence;

/// <summary>
/// Runs SQLite's <c>PRAGMA integrity_check</c> so corruption is detected and surfaced at startup
/// rather than causing silent data loss later. (Data-arch §5.9.)
/// </summary>
public static class CatalogIntegrity
{
    /// <summary>
    /// Check the database's structural integrity. Returns success when SQLite reports <c>ok</c>;
    /// otherwise a failure carrying the reported problems (or the malformed-image error).
    /// </summary>
    public static Result Check(DbConnection connection)
    {
        Guard.NotNull(connection);
        try
        {
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            using var reader = cmd.ExecuteReader();

            var problems = new List<string>();
            while (reader.Read())
            {
                var line = reader.GetString(0);
                if (!string.Equals(line, "ok", StringComparison.Ordinal))
                    problems.Add(line);
            }

            return problems.Count == 0
                ? Result.Success()
                : Result.Failure("catalog.integrity", $"Database integrity check failed: {string.Join("; ", problems)}");
        }
        catch (SqliteException ex)
        {
            // A malformed image throws rather than returning rows.
            return Result.Failure("catalog.integrity", $"Database integrity check failed: {ex.Message}");
        }
    }
}
