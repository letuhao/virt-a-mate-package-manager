using System.Collections.Generic;
using VarVault.App.Services;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>
/// GA-3 + GA-4 · The shell pulls rail badges (proposals/health/missing) and log-dock status (tier fill,
/// index progress) from the live-feeds seam. (18-gap GA-3/GA-4.)
/// </summary>
[Trait("Category", TestCategories.Unit)]
public class ShellLiveStateTests
{
    private sealed class FakeFeeds(ShellLiveSnapshot snap) : IShellLiveFeeds
    {
        public Task<ShellLiveSnapshot> SnapshotAsync(CancellationToken ct = default) => Task.FromResult(snap);
    }

    private static ShellViewModel Shell(ShellLiveSnapshot snap) =>
        new(new Dictionary<string, object>(), initial: "library", feeds: new FakeFeeds(snap));

    [Fact]
    public async Task Badges_populate_from_services()
    {
        var shell = Shell(new ShellLiveSnapshot(7, 725, 1203, "Storage T1 70%", null));
        await shell.RefreshLiveStateAsync();

        Assert.Equal(7, Badge(shell, "proposals"));
        Assert.Equal(725, Badge(shell, "health"));
        Assert.Equal(1203, Badge(shell, "missing"));
    }

    [Fact]
    public async Task Zero_count_clears_the_badge()
    {
        var shell = Shell(new ShellLiveSnapshot(0, 0, 0, null, null));
        await shell.RefreshLiveStateAsync();

        Assert.Null(Badge(shell, "proposals"));
        Assert.Null(Badge(shell, "health"));
        Assert.Null(Badge(shell, "missing"));
    }

    [Fact]
    public async Task Log_dock_reflects_tier_summary_and_index_status()
    {
        var shell = Shell(new ShellLiveSnapshot(0, 0, 0, "Storage T1 70% · T2 95%", "Indexing T3 · 18204/26455"));
        await shell.RefreshLiveStateAsync();

        Assert.Equal("Storage T1 70% · T2 95%", shell.TierSummary);
        Assert.Equal("Indexing T3 · 18204/26455", shell.IndexStatus);
    }

    [Trait("Category", TestCategories.Integration)]
    [Fact]
    public async Task Real_feeds_snapshot_from_the_composed_host()
    {
        await using var host = TestHost.Create(withPersistence: true);
        using var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .CreateScope(host.Host.Services);
        var sp = scope.ServiceProvider;
        var feeds = new ShellLiveFeeds(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(sp),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<VarVault.Sdk.Indexer.IIndexerClient>(sp));

        var snap = await feeds.SnapshotAsync();
        // Empty catalog → zero counts, no crash. Proves the wire to real services.
        Assert.True(snap.ProposalCount >= 0);
        Assert.True(snap.MissingCount >= 0);
    }

    /// <summary>24-checklist B1 · Each poll runs in its own DI scope, so concurrent snapshots never share a
    /// DbContext (EF is not thread-safe). Before B1 the feeds held services off the shell's app-lifetime scope.</summary>
    [Trait("Category", TestCategories.Integration)]
    [Fact]
    public async Task Concurrent_snapshots_never_share_a_dbcontext()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var sf = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(host.Host.Services);
        var indexer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<VarVault.Sdk.Indexer.IIndexerClient>(host.Host.Services);
        var feeds = new ShellLiveFeeds(sf, indexer);

        // 30 concurrent polls: with a per-snapshot scope this never throws EF's "second operation on this context".
        var snaps = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => feeds.SnapshotAsync()));
        Assert.All(snaps, s => Assert.True(s.ProposalCount >= 0));
    }

    [Trait("Category", TestCategories.Unit)]
    [Fact]
    public async Task Expensive_badge_counts_are_cached_across_ticks()
    {
        await using var host = TestHost.Create(withPersistence: true);
        var sf = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(host.Host.Services);
        var feeds = new ShellLiveFeeds(sf);

        // Tick 1 refreshes expensive badges; ticks 2–4 must not zero them out.
        var first = await feeds.SnapshotAsync();
        var mid = await feeds.SnapshotAsync();
        Assert.Equal(first.ProposalCount, mid.ProposalCount);
        Assert.Equal(first.HealthCount, mid.HealthCount);
    }

    private static int? Badge(ShellViewModel shell, string id) =>
        shell.RailItems.First(r => r.Id == id).Badge;
}
