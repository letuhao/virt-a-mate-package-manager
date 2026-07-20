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

    // Card badges + meta the mockup shows (audit 25 §5.2 "gallery cards are bare"). (doc 26 · G-3)
    public string StorageClass => entry.StorageClass;
    public bool IsFavorite => entry.IsFavorite;
    public bool IsSingleCopy => entry.IsSingleCopy;
    public bool HasMissingDeps => entry.HasMissingDeps;
    public bool IsActive => entry.IsActive;
    public string TierLabel => entry.Tier is { } t ? $"T{t}" : "";
    public string SizeText => entry.TotalSize >= 1L << 30
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{entry.TotalSize / (double)(1L << 30):F1} GB")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{entry.TotalSize / (double)(1L << 20):F0} MB");

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
