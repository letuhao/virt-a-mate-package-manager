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
    void OpenRescue();
    void OpenOnboarding();
    void OpenMigratePlan();
    void OpenVarDetail(long packageId);
    void OpenAlias(string missingRef);
    void OpenConfirmDelete(IReadOnlyList<ConfirmItem> items);
    void OpenFix(long varFileId, string? codepage);
    void OpenDupeReview(VarVault.Sdk.Library.DuplicateGroup group);
    void OpenPresetEdit(long presetId, string name);
}

/// <summary>Default launcher: resolves dialog VMs from the app service provider and shows them. (GD/GE.)</summary>
public sealed class DialogLauncher(IServiceProvider services, IDialogService dialogs, Action? afterRepoAdded = null)
    : IDialogLauncher
{
    public void OpenAddRepo() =>
        dialogs.Show(new AddRepoViewModel(services.GetRequiredService<Sdk.Repositories.IRepositoryService>(), afterRepoAdded));

    public void OpenRescue() =>
        dialogs.Show(new RescueViewModel(services.GetRequiredService<Sdk.Activation.IActivationService>()));

    public void OpenOnboarding() =>
        dialogs.Show(new OnboardingViewModel(services.GetService<Sdk.Library.IOnboardingService>()));

    public void OpenMigratePlan() =>
        dialogs.Show(new MigrateViewModel(services.GetRequiredService<Sdk.Library.ITieringService>()));

    public void OpenVarDetail(long packageId)
    {
        var vm = new VarDetailViewModel(services.GetRequiredService<Sdk.Library.IPackageDetailQuery>());
        _ = vm.LoadAsync(packageId);
        dialogs.Show(vm);
    }

    public void OpenAlias(string missingRef)
    {
        var vm = new AliasViewModel(services.GetRequiredService<Sdk.Library.IAliasService>());
        vm.MissingRef = missingRef;
        dialogs.Show(vm);
    }

    public void OpenConfirmDelete(IReadOnlyList<ConfirmItem> items)
    {
        var vm = new ConfirmDeleteViewModel(services.GetRequiredService<Sdk.Library.ILibraryActionService>());
        vm.SetItems(items);
        dialogs.Show(vm);
    }

    public void OpenFix(long varFileId, string? codepage)
    {
        var vm = new FixEncodingViewModel(services.GetRequiredService<Sdk.Library.IHealthService>())
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
        _ = vm.RefreshPreviewAsync();
        dialogs.Show(vm);
    }
}
