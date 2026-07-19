using System.IO;
using VarVault.Infrastructure.Indexing;
using VarVault.TestKit;

namespace VarVault.Infrastructure.Tests;

/// <summary>The filesystem watcher raises an event when a .var is dropped into a repository. (1.30.)</summary>
[Trait("Category", TestCategories.Integration)]
public sealed class RepositoryWatcherTests
{
    [Fact]
    public async Task Dropping_a_var_raises_a_change_event()
    {
        using var dir = new TempDirectory();
        var signalled = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watcher = new RepositoryWatcher(dir.Path);
        watcher.VarChanged += path => signalled.TrySetResult(path);
        watcher.Enabled = true;

        // Drop a var into the watched repo.
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "Creator.Pkg.1.var"), "x");

        var completed = await Task.WhenAny(signalled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(signalled.Task, completed); // the event fired within the bound
        Assert.EndsWith("Creator.Pkg.1.var", await signalled.Task, StringComparison.Ordinal);
    }

    [Fact]
    public void Watcher_starts_disabled_and_toggles()
    {
        using var dir = new TempDirectory();
        using var watcher = new RepositoryWatcher(dir.Path);
        Assert.False(watcher.Enabled);
        watcher.Enabled = true;
        Assert.True(watcher.Enabled);
    }
}
