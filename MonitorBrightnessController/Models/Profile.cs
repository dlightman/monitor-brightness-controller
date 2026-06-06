namespace MonitorBrightnessController.Models;

/// <summary>
/// A named brightness preset mapping monitors (by device path) to brightness values.
/// </summary>
public record Profile
{
    /// <summary>Profile name (1-64 chars, [a-zA-Z0-9_-]).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Map of monitor device path to brightness value (0-100).</summary>
    public IReadOnlyDictionary<string, int> MonitorBrightnessMap { get; init; }
        = new Dictionary<string, int>();
}
