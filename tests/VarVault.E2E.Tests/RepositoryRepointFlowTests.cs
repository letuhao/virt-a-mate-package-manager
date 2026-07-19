using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// Repository re-point with the volume-serial guard: a same-serial re-point (drive-letter shuffle /
/// portable catalog) succeeds; a different-serial one is refused and marks the repo read-only.
/// (Checklist 1.4/BE-R4, X.10.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class RepositoryRepointFlowTests
{
    [Fact]
    public async Task Same_serial_repoint_succeeds_and_different_serial_is_blocked()
    {
        var profiler = new FakeProfiler();
        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.RemoveAll<IDriveProfiler>();
            services.AddSingleton<IDriveProfiler>(profiler);
        });

        using var driveA1 = new TempDirectory();
        using var driveA2 = new TempDirectory();
        using var driveB = new TempDirectory();
        profiler.SerialFor = path => path.Contains(Path.GetFileName(driveB.Path), StringComparison.Ordinal) ? "SERIAL-B" : "SERIAL-A";

        var service = host.Get<IRepositoryService>();
        var registered = await service.RegisterAsync(new RegisterRepositoryRequest("Repo", driveA1.Path));
        Assert.True(registered.IsSuccess);
        var repoId = registered.Value.Id;

        // Same-serial re-point (drive-letter shuffle) → succeeds, path updated. (X.10)
        var good = await service.RepointAsync(repoId, driveA2.Path);
        Assert.True(good.IsSuccess, good.Error.ToString());
        Assert.Equal(Path.GetFullPath(driveA2.Path), good.Value.MountPath);

        // Different-serial re-point → refused. (1.4/BE-R4)
        var bad = await service.RepointAsync(repoId, driveB.Path);
        Assert.True(bad.IsFailure);
        Assert.Equal("repo.repoint.serial", bad.Error.Code);

        var list = await service.ListAsync();
        Assert.Equal(Path.GetFullPath(driveA2.Path), list.Single().MountPath); // unchanged by the blocked re-point
    }

    private sealed class FakeProfiler : IDriveProfiler
    {
        public Func<string, string?> SerialFor { get; set; } = _ => "SERIAL-A";
        public DriveProfile Profile(string path) => new(MediaType.Ssd, 1_000_000, 500_000, SerialFor(path));
        public (long? CapacityBytes, long? FreeBytes) GetCapacity(string path) => (1_000_000, 500_000);
    }
}
