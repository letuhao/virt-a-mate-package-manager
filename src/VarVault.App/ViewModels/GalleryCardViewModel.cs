using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VarVault.App.Services;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>
/// One gallery card: a library row plus its <b>extracted preview thumbnail</b>, loaded off the UI thread via
/// <see cref="ThumbnailLoader{TImage}"/> (decoded from the packed thumbnail store the indexer populates).
/// Falls back to a type placeholder when the var has no preview image. (Gallery / 1.35.)
/// </summary>
public sealed partial class GalleryCardViewModel(PackageListEntry entry, ThumbnailLoader<Bitmap>? loader)
    : ObservableObject
{
    public PackageListEntry Entry => entry;
    public long PackageId => entry.PackageId;
    public string PackageName => entry.PackageName;
    public string Creator => entry.Creator;
    public string PrimaryType => entry.PrimaryType;

    [ObservableProperty] private Bitmap? _thumbnail;

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>Load (and decode) the extracted preview thumbnail for this package, if one was indexed.</summary>
    public async Task LoadAsync()
    {
        if (loader is null)
            return;
        try
        {
            Thumbnail = await loader.LoadAsync(PackageId).ConfigureAwait(true);
        }
        catch
        {
            Thumbnail = null; // decode failure → placeholder
        }
        OnPropertyChanged(nameof(HasThumbnail));
    }
}
