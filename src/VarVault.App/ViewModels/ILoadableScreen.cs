using System.Threading;
using System.Threading.Tasks;

namespace VarVault.App.ViewModels;

/// <summary>
/// A shell screen that populates itself on demand. The shell calls <see cref="LoadAsync"/> when the screen
/// becomes active (navigation) and again after a background index completes, so every screen shows its data
/// without the user first hitting a Refresh button. (doc 26 · G-0 — fixes the "blank on arrival" defect.)
/// </summary>
public interface ILoadableScreen
{
    Task LoadAsync(CancellationToken cancellationToken = default);
}
