using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using VarVault.Infrastructure.Persistence;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>
/// 0.24 — the query planner uses the intended indexes (no full scans / temp B-trees for the
/// hot lookups and the gallery sort orders defined in data-arch §6/§5.8).
/// </summary>
[Trait("Category", TestCategories.Integration)]
public sealed class SchemaIndexTests
{
    [Fact]
    public void Identity_lookup_uses_the_unique_index()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var plan = QueryPlan(db, "SELECT * FROM Package WHERE IdentityKey = 'X.Y.1';");
        Assert.Contains("IX_Package_IdentityKey", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Varfile_lookup_by_repo_path_uses_the_unique_index()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var plan = QueryPlan(db,
            "SELECT * FROM VarFile WHERE RepositoryId = '00000000-0000-0000-0000-000000000000' AND RelativePath = 'a.var';");
        Assert.Contains("IX_VarFile_RepositoryId_RelativePath", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_signature_dedup_lookup_uses_an_index()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var plan = QueryPlan(db, "SELECT * FROM VarFile WHERE ContentSignature = 'sig';");
        Assert.Contains("IX_VarFile_ContentSignature", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Gallery_class_sort_uses_composite_index_without_temp_btree()
    {
        using var fx = new SqliteTestDatabase();
        using var db = fx.NewContext();
        var plan = QueryPlan(db,
            "SELECT PackageId FROM PackageListItem ORDER BY Class, LastUsedAt, PackageId;");
        Assert.Contains("IX_PackageListItem_Class_LastUsedAt_PackageId", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("USE TEMP B-TREE", plan, StringComparison.OrdinalIgnoreCase);
    }

    private static string QueryPlan(VarVaultDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
        using var reader = cmd.ExecuteReader();
        var lines = new List<string>();
        while (reader.Read())
            lines.Add(reader.GetValue(reader.FieldCount - 1)?.ToString() ?? string.Empty);
        return string.Join(" | ", lines);
    }
}
