using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Converts a boolean to <see cref="Visibility"/> with inverted semantics: <c>true</c>
/// becomes <see cref="Visibility.Collapsed"/> and <c>false</c> becomes
/// <see cref="Visibility.Visible"/>. Used to hide the monitor list while the
/// "no controllable monitors" message is shown.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        return flag ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}
