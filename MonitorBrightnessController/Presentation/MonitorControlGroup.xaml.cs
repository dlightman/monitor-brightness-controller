using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Interaction logic for MonitorControlGroup.xaml. Renders a single monitor's brightness
/// controls (label, slider, text input, validation/error messages) and forwards commit
/// gestures to the bound <see cref="MonitorControlViewModel"/>.
/// </summary>
public partial class MonitorControlGroup : UserControl
{
    /// <summary>
    /// Tracks whether a slider drag is in progress. When true, brightness is committed only
    /// on drag completion to avoid spamming DDC/CI during a drag. When false (single click on
    /// track, arrow keys, etc.), brightness is committed immediately via ValueChanged.
    /// </summary>
    private bool _isDragging;

    /// <summary>Creates the control.</summary>
    public MonitorControlGroup()
    {
        InitializeComponent();
    }

    private MonitorControlViewModel? ViewModel => DataContext as MonitorControlViewModel;

    private void BrightnessSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDragging = false;
        // Commit the final value when the user releases the slider thumb.
        ViewModel?.CommitFromSlider();
    }

    private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // During a drag, DragCompleted handles the commit. For non-drag interactions
        // (clicking the track, arrow keys, Page Up/Down), commit immediately.
        if (_isDragging)
        {
            return;
        }

        // Check if the thumb is currently being dragged by looking at IsMouseCaptured on the
        // thumb. If the slider's thumb has mouse capture, a drag is starting.
        if (sender is Slider slider)
        {
            var track = slider.Template?.FindName("PART_Track", slider) as Track;
            if (track?.Thumb?.IsDragging == true)
            {
                _isDragging = true;
                return;
            }
        }

        // Not a drag — commit immediately so single-clicks and keyboard changes apply.
        ViewModel?.CommitFromSlider();
    }

    private void BrightnessInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        // Push the typed text into the bound property, then validate/commit it.
        if (sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        ViewModel?.CommitFromText();
        e.Handled = true;
    }

    private void BrightnessInput_LostFocus(object sender, RoutedEventArgs e)
    {
        // The LostFocus binding has already pushed the text to the view model; validate it.
        ViewModel?.CommitFromText();
    }
}
