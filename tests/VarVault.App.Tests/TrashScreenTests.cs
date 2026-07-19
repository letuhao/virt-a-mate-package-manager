using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Library;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-11 · Trash screen lists trashed items + restore/purge. (16-checklist SCR-11.)</summary>
[Trait("Category", TestCategories.Unit)]
public class TrashScreenTests
{
    private sealed class StubTrash : ITrashQueryService
    {
        public Task<IReadOnlyList<TrashItemDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TrashItemDto>>([new TrashItemDto("id1", @"D:\OldLook.v1.1.var", "superseded", DateTime.UtcNow, 120)]);
        public Task<Result> RestoreAsync(string id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<Result> PurgeAsync(string id, CancellationToken ct = default) => Task.FromResult(Result.Success());
        public Task<IReadOnlyList<BackupDto>> ListBackupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BackupDto>>([]);
        public Task<Result<BackupDto>> BackupNowAsync(CancellationToken ct = default) => Task.FromResult(Result.Success(new BackupDto("b", DateTime.UtcNow, 1)));
    }

    [AvaloniaFact]
    public async Task Lists_trash_items()
    {
        var vm = new TrashViewModel(new StubTrash());
        await vm.LoadAsync();
        Assert.Single(vm.Items);
        var view = new TrashView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(@"D:\OldLook.v1.1.var", view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
    }
}
