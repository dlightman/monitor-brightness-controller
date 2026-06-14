using System.Globalization;
using MonitorBrightnessController.Application;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Pure, hardware-independent logic for keeping a brightness slider and its text input in
/// sync. Isolated here so that the bidirectional synchronization behaviour (design
/// Property 4) can be exercised directly by property-based tests without constructing any
/// WPF controls or view models.
/// </summary>
public static class BrightnessSync
{
    /// <summary>
    /// Formats a brightness value as the canonical text representation shown in the text input.
    /// Used when reflecting a slider position into the text box (Requirement 2.4).
    /// </summary>
    /// <param name="brightness">The brightness value in the range [0, 100].</param>
    /// <returns>The invariant-culture string representation of <paramref name="brightness"/>.</returns>
    public static string ToText(int brightness) =>
        brightness.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Attempts to parse committed text input into a valid brightness value, applying the
    /// same validation rules used everywhere else in the application
    /// (<see cref="MonitorService.TryParseBrightness"/>): integer only, range [0, 100].
    /// Used when reflecting a committed text value into the slider position (Requirement 2.5).
    /// </summary>
    /// <param name="text">The committed text input.</param>
    /// <param name="brightness">The parsed brightness value when the input is valid; otherwise 0.</param>
    /// <returns>True when the text represents a valid brightness value; otherwise false.</returns>
    public static bool TryParseText(string? text, out int brightness) =>
        MonitorService.TryParseBrightness(text, out brightness);
}
