using System.IO;
using VarVault.Sdk.Repositories;
using VarVault.TestKit;

namespace VarVault.E2E.Tests;

/// <summary>
/// End-to-end repository registration through the composed host: profile the drive, assign a tier,
/// persist, list, reject overlaps, and toggle enabled. (Checklist 1.1/1.2/1.3/1.7/1.9.)
/// </summary>
[Trait("Category", TestCategories.E2E)]
public sealed class RepositoryRegistrationFlowTests
{
    [Fact]
    public async Task Registers_profiles_and_lists_a_repository()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var service = host.Get<IRepositoryService>();

        var result = await service.RegisterAsync(new RegisterRepositoryRequest("My SSD", repoDir.Path));
        Assert.True(result.IsSuccess, result.Error.ToString());

        var info = result.Value;
        Assert.Equal("My SSD", info.Name);
        Assert.InRange(info.Tier, 1, 3);
        Assert.True(info.IsEnabled);
        Assert.True(info.CapacityBytes > 0); // 1.7 live capacity

        var list = await service.ListAsync();
        Assert.Single(list);
        Assert.Equal(info.Id, list[0].Id);
    }

    [Fact]
    public async Task Rejects_a_repository_overlapping_an_existing_one()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var service = host.Get<IRepositoryService>();

        await service.RegisterAsync(new RegisterRepositoryRequest("Root", repoDir.Path));

        var childPath = Path.Combine(repoDir.Path, "child");
        Directory.CreateDirectory(childPath);
        var overlap = await service.RegisterAsync(new RegisterRepositoryRequest("Child", childPath));

        Assert.True(overlap.IsFailure); // 1.2
        Assert.Equal("repo.path.overlap", overlap.Error.Code);
    }

    [Fact]
    public async Task Rejects_a_missing_folder()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var service = host.Get<IRepositoryService>();
        var result = await service.RegisterAsync(new RegisterRepositoryRequest("Ghost", @"Z:\does\not\exist"));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Enable_disable_persists()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var repoDir = new TempDirectory();
        var service = host.Get<IRepositoryService>();

        var info = (await service.RegisterAsync(new RegisterRepositoryRequest("R", repoDir.Path))).Value;
        Assert.True(await service.SetEnabledAsync(info.Id, false)); // 1.9

        var list = await service.ListAsync();
        Assert.False(list.Single().IsEnabled);
    }
}
