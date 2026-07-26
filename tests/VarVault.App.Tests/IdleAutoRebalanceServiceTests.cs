using Microsoft.Extensions.DependencyInjection;
using VarVault.App.Services;
using VarVault.Sdk.Settings;
using VarVault.Sdk.Threading;
using VarVault.TestKit;

namespace VarVault.App.Tests;

[Trait("Category", TestCategories.Unit)]
public class IdleAutoRebalanceServiceTests
{
    [Fact]
    public async Task Disabled_setting_enqueues_nothing()
    {
        var queue = new FakeQueue();
        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.AddSingleton<IJobQueue>(queue);
        });
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>()
            .SetBoolAsync(SettingKeys.AutoRebalance, false);

        var svc = new IdleAutoRebalanceService(host.Host.Services.GetRequiredService<IServiceScopeFactory>(), queue);
        Assert.Null(await svc.TryEnqueueAsync());
        Assert.Empty(queue.Enqueued);
    }

    [Fact]
    public async Task Enabled_with_empty_plan_enqueues_nothing()
    {
        var queue = new FakeQueue();
        await using var host = TestHost.Create(withPersistence: true, configure: services =>
        {
            services.AddSingleton<IJobQueue>(queue);
        });
        using var scope = host.Host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>()
            .SetBoolAsync(SettingKeys.AutoRebalance, true);

        var svc = new IdleAutoRebalanceService(host.Host.Services.GetRequiredService<IServiceScopeFactory>(), queue);
        Assert.Null(await svc.TryEnqueueAsync());
        Assert.Empty(queue.Enqueued);
    }

    private sealed class FakeQueue : IJobQueue
    {
        public List<string> Enqueued { get; } = [];
        public IReadOnlyList<JobHandle> Active => [];
        public JobHandle Enqueue(string name, Func<JobContext, Task> work)
        {
            Enqueued.Add(name);
            return new JobHandle(name);
        }
    }
}
