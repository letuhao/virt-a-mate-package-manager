using VarVault.Common;
using VarVault.Sdk.Presets;
using VarVault.App.ViewModels;
using VarVault.TestKit;

namespace VarVault.App.Tests;

/// <summary>DLG-7 · Add member persists + refreshes preview. (16-checklist DLG-7.)</summary>
[Trait("Category", TestCategories.Unit)]
public class PresetEditDialogTests
{
    private sealed class StubPresets : IPresetService
    {
        public bool Added;
        public Task<Result<PresetInfo>> AddMemberAsync(long id, string r, CancellationToken ct = default) { Added = true; return Task.FromResult(Result.Success(new PresetInfo(id, "p", 1))); }
        public Task<ActivationPreview?> PreviewActivationAsync(long id, CancellationToken ct = default) => Task.FromResult<ActivationPreview?>(new ActivationPreview(1, 3, []));
        public Task<Result<PresetInfo>> RemoveMemberAsync(long id, string r, CancellationToken ct = default) => Task.FromResult(Result.Success(new PresetInfo(id, "p", 0)));
        public Task<IReadOnlyList<string>> MembersAsync(long id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<PresetInfo>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PresetInfo>>([]);
        public Task<Result<PresetInfo>> CreateAsync(string n, IEnumerable<string> m, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RefreshMemberResolutionsAsync(long id, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Add_member_persists_and_previews()
    {
        var stub = new StubPresets();
        var vm = new PresetEditViewModel(stub) { PresetId = 1, NewMemberRef = "A.B.1" };
        await vm.AddMemberCommand.ExecuteAsync(null);
        Assert.True(stub.Added);
        Assert.Equal("Member added", vm.StatusMessage);
        Assert.NotNull(vm.Preview);
    }
}
