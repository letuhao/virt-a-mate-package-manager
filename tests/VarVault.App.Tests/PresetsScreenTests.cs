using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VarVault.Common;
using VarVault.Sdk.Presets;
using VarVault.App.ViewModels;
using VarVault.App.Views;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>SCR-4 · Presets screen lists presets and previews activation. (16-checklist SCR-4.)</summary>
[Trait("Category", TestCategories.Unit)]
public class PresetsScreenTests
{
    private sealed class StubPresets : IPresetService
    {
        public Task<IReadOnlyList<PresetInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PresetInfo>>([new PresetInfo(1, "Cinematic set", 241), new PresetInfo(2, "Quick play", 86)]);
        public Task<ActivationPreview?> PreviewActivationAsync(long id, CancellationToken ct = default) =>
            Task.FromResult<ActivationPreview?>(new ActivationPreview(241, 279, ["Gone.Missing.1"]));
        public Task<Result<PresetInfo>> CreateAsync(string n, IEnumerable<string> m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PresetInfo>> AddMemberAsync(long id, string r, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public async Task Lists_presets_and_previews_selection()
    {
        var vm = new PresetsViewModel(new StubPresets());
        await vm.LoadAsync();
        Assert.Equal(2, vm.Presets.Count);

        var view = new PresetsView { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Selected = vm.Presets[0];
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(vm.Preview);
        Assert.Equal(279, vm.Preview!.TotalWithClosure);

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Cinematic set", texts);
    }
}
