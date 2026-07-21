using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VarVault.App.Services;
using VarVault.Sdk.Library;

namespace VarVault.App.ViewModels;

/// <summary>Actionable direct-dependency card with a lazily decoded package thumbnail.</summary>
public sealed partial class DependencyCardViewModel(
    DependencyEdgeDto edge,
    ThumbnailLoader<Bitmap>? thumbnails) : ObservableObject
{
    private bool _loaded;

    public DependencyEdgeDto Edge => edge;
    public string RequestedRefRaw => edge.RequestedRefRaw;
    public DependencyResolutionState State => edge.State;
    public ResolvedPackageCard? ResolvedPackage => edge.ResolvedPackage;

    [ObservableProperty] private Bitmap? _thumbnail;
    public bool HasThumbnail => Thumbnail is not null;
    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded || thumbnails is null || edge.ResolvedPackage is not { } package)
            return;
        _loaded = true;
        try
        {
            Thumbnail = await thumbnails.LoadAsync(package.PackageId, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            Thumbnail = null;
        }
    }
}
