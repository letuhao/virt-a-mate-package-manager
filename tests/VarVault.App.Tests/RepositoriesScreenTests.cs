using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Repositories;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-3 · Repositories screen renders repo cards. (16-checklist SCR-3.)</summary>
[Trait("Category", TestCategories.Unit)]
public class RepositoriesScreenTests
{
    private sealed class StubRepos(IReadOnlyList<RepositoryInfo> list) : IRepositoryService
    {
        public Task<IReadOnlyList<RepositoryInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult(list);
        public Task<Result<RepositoryInfo>> RegisterAsync(RegisterRepositoryRequest r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(Guid id, bool e, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> RenameAsync(Guid id, string name, CancellationToken ct = default) => Task.FromResult(true);
        public Task<RepositoryInfo?> RefreshCapacityAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<RepositoryInfo>> RepointAsync(Guid id, string p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> BenchmarkAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<RepositoryInfo?> SetTierAsync(Guid id, int tier, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public async Task Loads_and_renders_repo_cards()
    {
        var repos = new StubRepos(
        [
            new RepositoryInfo(Guid.NewGuid(), "Samsung 990 Pro", @"D:\vars", "Nvme", 1, true, true, 2000, 600, null),
            new RepositoryInfo(Guid.NewGuid(), "Archive USB", @"G:\vars", "Removable", 3, false, true, 8000, 5000, null),
        ]);
        var vm = new RepositoriesViewModel(repos);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Repositories.Count);

        var view = new RepositoriesView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Samsung 990 Pro", texts);
        Assert.Contains("Archive USB", texts);
    }
}
