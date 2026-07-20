using VarVault.App.ViewModels;
using VarVault.Common;
using VarVault.Sdk.Repositories;

namespace VarVault.App.Tests;

/// <summary>The Edit-repository dialog renames and/or re-points a repo, and surfaces the re-point serial guard.</summary>
public sealed class EditRepoViewModelTests
{
    private static RepositoryInfo Repo(string name = "Old", string path = @"C:\repo") =>
        new(System.Guid.NewGuid(), name, path, "Ssd", 1, true, true, null, null, null);

    [Fact]
    public async Task Rename_only_calls_rename_and_refreshes()
    {
        var svc = new RecordingRepos();
        var refreshed = false;
        var vm = new EditRepoViewModel(svc, Repo(), () => refreshed = true) { Name = "New name" };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(svc.Renamed);
        Assert.Equal("New name", svc.Renamed[0].Name);
        Assert.Empty(svc.Repointed);           // path unchanged → no re-point
        Assert.True(vm.Saved);
        Assert.False(vm.IsError);
        Assert.True(refreshed);
    }

    [Fact]
    public async Task Repoint_failure_surfaces_the_serial_guard_and_does_not_mark_saved()
    {
        var svc = new RecordingRepos
        {
            RepointResult = Result.Failure<RepositoryInfo>("repo.repoint.serial", "different serial — refusing to re-point"),
        };
        var vm = new EditRepoViewModel(svc, Repo()) { Path = @"D:\other" };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Single(svc.Repointed);
        Assert.True(vm.IsError);
        Assert.Contains("serial", vm.Message);
        Assert.False(vm.Saved);
    }

    [Fact]
    public async Task No_change_is_a_noop()
    {
        var svc = new RecordingRepos();
        var vm = new EditRepoViewModel(svc, Repo());   // Name/Path untouched
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Empty(svc.Renamed);
        Assert.Empty(svc.Repointed);
        Assert.False(vm.Saved);
        Assert.Contains("Nothing", vm.Message);
    }

    private sealed class RecordingRepos : IRepositoryService
    {
        public System.Collections.Generic.List<(System.Guid Id, string Name)> Renamed { get; } = [];
        public System.Collections.Generic.List<(System.Guid Id, string Path)> Repointed { get; } = [];
        public Result<RepositoryInfo> RepointResult { get; set; } =
            Result.Success(new RepositoryInfo(System.Guid.NewGuid(), "R", @"C:\repo", "Ssd", 1, true, true, null, null, null));

        public Task<bool> RenameAsync(System.Guid id, string name, CancellationToken ct = default)
        { Renamed.Add((id, name)); return Task.FromResult(true); }
        public Task<Result<RepositoryInfo>> RepointAsync(System.Guid id, string p, CancellationToken ct = default)
        { Repointed.Add((id, p)); return Task.FromResult(RepointResult); }

        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<bool> RemoveAsync(System.Guid id, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<bool> SetEnabledAsync(System.Guid id, bool e, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<RepositoryInfo?> RefreshCapacityAsync(System.Guid id, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(System.Guid id, CancellationToken ct = default) => throw new System.NotSupportedException();
        public Task<RepositoryInfo?> SetTierAsync(System.Guid id, int tier, CancellationToken ct = default) => throw new System.NotSupportedException();
    }
}
