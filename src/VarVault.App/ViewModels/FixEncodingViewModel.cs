using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>DLG-4 · Fix encoding → UTF-8 (new var, original retained). (16-checklist DLG-4.)</summary>
public sealed partial class FixEncodingViewModel(IHealthService health) : ObservableObject
{
    [ObservableProperty] private long _varFileId;
    [ObservableProperty] private string? _codepage;
    [ObservableProperty] private string _before = "Custom/è¡£è£…/ã‚¹ã‚«ãƒ¼ãƒˆ.vam";
    [ObservableProperty] private string _after = "Custom/衣装/スカート.vam";
    [ObservableProperty] private string? _resultMessage;

    [RelayCommand]
    public async Task FixAsync(CancellationToken cancellationToken = default)
    {
        var result = await health.FixAsync(VarFileId, cancellationToken).ConfigureAwait(true);
        ResultMessage = result.IsSuccess ? "Fixed → UTF-8 (original retained)" : result.Error.Message;
    }
}
