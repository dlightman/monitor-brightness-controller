using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Minimal base class providing <see cref="INotifyPropertyChanged"/> support for view models.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the given property name.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed. Supplied automatically by the compiler.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
