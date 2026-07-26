using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VarVault.Domain.Entities;
using VarVault.Infrastructure.Persistence;
using VarVault.Sdk.Library;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

[Trait("Category", TestCategories.Integration)]
public sealed class VamLogUsageImporterTests
{
    [Fact]
    public async Task Import_records_VamLogImport_Load_events_for_library_matches()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = host.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VarVaultDbContext>();
        db.Packages.Add(new Package
        {
            Id = 1, VarName = "Creator.Look.1", IdentityKey = "CREATOR.LOOK.1", Creator = "Creator",
            PackageName = "Look", VersionToken = "1", VersionSort = 1,
            FirstSeenAt = DateTime.UtcNow, LastIndexedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var importer = scope.ServiceProvider.GetRequiredService<IVamLogUsageImporter>();
        const string log = """
            Missing addon package Creator.Hair.2 that Creator.Look.1 depends on
            Failed to load Creator.Look.1.var
            """;

        var preview = await importer.PreviewAsync(log);
        Assert.True(preview.MatchedInLibrary >= 1);
        Assert.True(preview.Unmatched >= 1);

        var result = await importer.ImportAsync(log);
        Assert.True(result.EventsRecorded >= 1);

        var ev = await db.UsageEvents.AsNoTracking().FirstAsync(e => e.PackageId == 1);
        Assert.Equal(UsageKind.Load, ev.Kind);
        Assert.Equal(UsageSource.VamLogImport, ev.Source);
    }
}
