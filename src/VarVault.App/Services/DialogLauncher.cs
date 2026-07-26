using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using VarVault.App.ViewModels;

namespace VarVault.App.Services;

/// <summary>
/// GD/GE · The seam screens use to open dialogs. Builds each dialog view-model from the composition root's
/// services and shows it through the <see cref="IDialogService"/>. Screens depend only on this interface —
/// they never new-up a dialog VM or touch the service provider. (18-gap G-D/G-E.)
/// </summary>
public interface IDialogLauncher
{
    void OpenAddRepo();
    void OpenEditRepo(Sdk.Repositories.RepositoryInfo repo, System.Action? onSaved = null);
    void OpenRescue();
    void OpenOnboarding();
    void OpenMigratePlan();
    void OpenVarDetail(long packageId);
    void OpenVarDetail(long packageId, bool push);
    void OpenAlias(string missingRef, Action? onSaved = null, string? suggestedOwnedQuery = null);
    void OpenManageAliases(Action? onChanged = null, string? focusMissingRef = null, string? suggestedOwnedQuery = null);
    void OpenConfirmDelete(IReadOnlyList<ConfirmItem> items, int reverseDepCount = 0);
    void OpenFix(long varFileId, string? codepage);
    void OpenDupeReview(VarVault.Sdk.Library.DuplicateGroup group);
    void OpenPresetEdit(long presetId, string name);
    void OpenEditMeta(long packageId, Action? onSaved = null);
}

/// <summary>Default launcher: resolves dialog VMs from the app service provider and shows them. (GD/GE.)</summary>
public sealed class DialogLauncher(
    IServiceProvider services, IDialogService dialogs,
    Action? afterRepoAdded = null, Action<string, Action?>? toast = null,
    EncodingFixJobRunner? encodingJobs = null)
    : IDialogLauncher
{
    public void OpenAddRepo() =>
        dialogs.Show(new AddRepoViewModel(
            services.GetRequiredService<Sdk.Repositories.IRepositoryService>(),
            afterRepoAdded,
            services.GetService<Sdk.Library.IMigrationService>(),
            services.GetService<Sdk.Threading.IJobQueue>()));

    public void OpenEditRepo(Sdk.Repositories.RepositoryInfo repo, System.Action? onSaved = null) =>
        dialogs.Show(new EditRepoViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>(), repo, onSaved));

    public void OpenRescue()
    {
        var vm = new RescueViewModel(
            services.GetRequiredService<Sdk.Activation.IActivationService>(),
            services.GetService<Sdk.Presets.IPresetService>(),
            services.GetService<Sdk.Library.IProfileService>());
        _ = vm.LoadAsync();
        dialogs.Show(vm);
    }

    public void OpenOnboarding() =>
        dialogs.Show(new OnboardingViewModel(services.GetService<Sdk.Library.IOnboardingService>()));

    public void OpenMigratePlan()
    {
        var vm = new MigrateViewModel(
            services.GetRequiredService<Sdk.Library.ITieringService>(),
            services.GetService<Sdk.Library.IMigrationService>(),
            services.GetService<Sdk.Repositories.IRepositoryService>());
        _ = vm.LoadPlanAsync();
        dialogs.Show(vm);
    }

    public void OpenVarDetail(long packageId) => OpenVarDetail(packageId, push: false);

    public void OpenVarDetail(long packageId, bool push)
    {
        var vm = new VarDetailViewModel(
            services.GetRequiredService<Sdk.Library.IPackageDetailQuery>(),
            this,
            services.GetService<IClipboard>() ?? new AvaloniaClipboard(),
            services.GetService<VarVault.Domain.Indexing.IThumbnailStore>(),
            VarVault.App.Composition.IndexerClientOverride.Current ?? services.GetService<Sdk.Indexer.IIndexerClient>(),
            services.GetService<Sdk.Library.IPlacementOverrideService>());
        _ = vm.LoadAsync(packageId);
        if (push)
            dialogs.Push(vm);
        else
            dialogs.Show(vm);
    }

    public void OpenAlias(string missingRef, Action? onSaved = null, string? suggestedOwnedQuery = null)
    {
        var vm = new AliasViewModel(
            services.GetRequiredService<Sdk.Library.IAliasService>(),
            services.GetService<Sdk.Library.ILibraryQueryService>(),
            close: () => dialogs.Back(),
            clipboard: services.GetService<IClipboard>() ?? new AvaloniaClipboard())
        {
            OnSaved = onSaved,
        };
        vm.MissingRef = missingRef;
        if (!string.IsNullOrWhiteSpace(suggestedOwnedQuery))
            vm.OwnedQuery = suggestedOwnedQuery;
        dialogs.Push(vm);
    }

    public void OpenManageAliases(Action? onChanged = null, string? focusMissingRef = null, string? suggestedOwnedQuery = null)
    {
        var vm = new ManageAliasesViewModel(
            services.GetRequiredService<Sdk.Library.IAliasService>(),
            services.GetService<Sdk.Library.ILibraryQueryService>(),
            services.GetService<IClipboard>() ?? new AvaloniaClipboard(),
            close: () => dialogs.Close())
        {
            OnChanged = onChanged,
        };
        if (!string.IsNullOrWhiteSpace(focusMissingRef))
            vm.MissingRef = focusMissingRef;
        if (!string.IsNullOrWhiteSpace(suggestedOwnedQuery))
            vm.OwnedQuery = suggestedOwnedQuery;
        _ = vm.LoadAsync();
        dialogs.Show(vm);
    }

    public void OpenConfirmDelete(IReadOnlyList<ConfirmItem> items, int reverseDepCount = 0)
    {
        var vm = new ConfirmDeleteViewModel(services.GetRequiredService<Sdk.Library.ILibraryActionService>())
        {
            OnDeleted = count => toast?.Invoke($"Moved {count} items to trash", null),
            ReverseDepCount = reverseDepCount,
        };
        vm.SetItems(items);
        dialogs.Show(vm);
    }

    public void OpenFix(long varFileId, string? codepage)
    {
        var vm = new FixEncodingViewModel(
            services.GetRequiredService<Sdk.Library.IHealthService>(),
            encodingJobs ?? services.GetService<EncodingFixJobRunner>())
        {
            VarFileId = varFileId,
            Codepage = codepage,
        };
        dialogs.Show(vm);
    }

    public void OpenDupeReview(Sdk.Library.DuplicateGroup group)
    {
        var vm = new DupeReviewViewModel(services.GetRequiredService<Sdk.Library.IReclaimService>());
        vm.SetGroup(group);
        dialogs.Show(vm);
    }

    public void OpenPresetEdit(long presetId, string name)
    {
        var vm = new PresetEditViewModel(services.GetRequiredService<Sdk.Presets.IPresetService>())
        {
            PresetId = presetId,
            Name = name,
        };
        dialogs.Show(vm);
        _ = LoadPresetEditAsync(vm);
    }

    private static async Task LoadPresetEditAsync(PresetEditViewModel vm)
    {
        await vm.LoadMembersAsync().ConfigureAwait(true);
        await vm.RefreshPreviewAsync().ConfigureAwait(true);
    }

    public void OpenEditMeta(long packageId, Action? onSaved = null)
    {
        var vm = new EditMetaViewModel(
            services.GetRequiredService<Sdk.Library.IVarMetaEditService>(),
            packageId,
            onSaved,
            close: () => dialogs.Back());
        _ = vm.LoadAsync();
        dialogs.Push(vm);
    }
}
