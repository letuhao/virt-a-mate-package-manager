using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VirtaMatePackageManager.Presentation.ViewModels;

/// <summary>
/// Base class for all ViewModels implementing INotifyPropertyChanged.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Sets the value of a property and raises PropertyChanged if the value changed.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Disposes resources. Override in derived classes to clean up.
    /// </summary>
    public virtual void Dispose()
    {
        // Override in derived classes if needed
    }
}

