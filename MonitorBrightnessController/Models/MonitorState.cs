namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents the in-memory state of a single detected monitor.
/// </summary>
public record MonitorState
{
    /// <summary>Unique numeric identifier assigned by enumeration order, starting at 1.</summary>
    public int MonitorIndex { get; init; }

    /// <summary>Human-readable monitor name (EDID name or "Monitor N" fallback).</summary>
    public string MonitorName { get; init; } = string.Empty;

    /// <summary>Windows device path used for deterministic ordering and profile mapping.</summary>
    public string DevicePath { get; init; } = string.Empty;

    /// <summary>Native physical monitor handle used for DDC/CI communication.</summary>
    public IntPtr PhysicalHandle { get; init; }

    /// <summary>Current brightness value 0-100, or null when unknown (read failed).</summary>
    public int? CurrentBrightness { get; init; }

    /// <summary>True when the monitor supports DDC/CI and can be controlled.</summary>
    public bool IsControllable { get; init; }

    /// <summary>Optional error message describing a communication problem.</summary>
    public string? ErrorMessage { get; init; }
}
