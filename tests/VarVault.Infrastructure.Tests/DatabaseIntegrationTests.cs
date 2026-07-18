using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public class DatabaseIntegrationTests
{
    [Fact]
    public void Test_database_fixture_opens_a_connectable_context()
    {
        using var db = new SqliteTestDatabase();
        using var context = db.NewContext();

        Assert.True(context.Database.CanConnect());
    }
}
