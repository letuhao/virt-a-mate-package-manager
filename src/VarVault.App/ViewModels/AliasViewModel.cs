using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-5 · Resolve missing dependency: map a missing ref to an owned package. (16-checklist DLG-5.)</summary>
public sealed partial class AliasViewModel(IAliasService aliases) : ObservableObject
{
    [ObservableProperty] private string? _missingRef;
    [ObservableProperty] private long _ownedPackageId;
    [ObservableProperty] private string _scope = "Global — apply everywhere, always";
    [ObservableProperty] private string? _statusMessage;

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(MissingRef))
            return;
        var result = await aliases.SetAsync(MissingRef!, OwnedPackageId, cancellationToken).ConfigureAwait(true);
        StatusMessage = result.IsSuccess ? "Alias saved" : result.Error.Message;
    }
}
