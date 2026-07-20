using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Re-benchmark re-detects the media type, so a drive that was mis-detected (e.g. an SSD read as HDD when the
/// seek-penalty probe couldn't open the volume without elevation) is corrected — and re-tiered accordingly.
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class RepositoryMediaTypeTests
{
    [Fact]
    public async Task Rebenchmark_refreshes_media_type_and_retiers()
    {
        var profiler = new SwitchableProfiler { Media = MediaType.Hdd };  // first seen (wrongly) as HDD
        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.RemoveAll<IDriveProfiler>();
            services.AddSingleton<IDriveProfiler>(profiler);
        });

        using var dir = new TempDirectory();
        var svc = host.Get<IRepositoryService>();

        var reg = await svc.RegisterAsync(new RegisterRepositoryRequest("R", dir.Path));
        Assert.True(reg.IsSuccess);
        Assert.Equal("Hdd", reg.Value.MediaType);
        var hddTier = reg.Value.Tier;

        // The drive is actually an SSD; now the profiler reports it correctly.
        profiler.Media = MediaType.Ssd;
        var benched = await svc.BenchmarkAsync(reg.Value.Id);

        Assert.NotNull(benched);
        Assert.Equal("Ssd", benched!.MediaType);      // media type corrected on re-benchmark (the bugfix)
        Assert.True(benched.Tier <= hddTier);          // an SSD is never tiered colder than it was as an HDD
    }

    private sealed class SwitchableProfiler : IDriveProfiler
    {
        public MediaType Media { get; set; } = MediaType.Hdd;
        public DriveProfile Profile(string path) => new(Media, 1_000_000_000, 500_000_000, "SERIAL-A");
        public (long? CapacityBytes, long? FreeBytes) GetCapacity(string path) => (1_000_000_000, 500_000_000);
    }
}
