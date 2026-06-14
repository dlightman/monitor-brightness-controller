namespace MonitorBrightnessController.Models;

/// <summary>
/// Persisted application settings: profiles, preferences, and last-used state.
/// </summary>
public record AppSettings
{
    /// <summary>All stored brightness profiles (maximum 50).</summary>
    public List<Profile> Profiles { get; init; } = new();

    /// <summary>When true, the last-applied profile is applied automatically on startup.</summary>
    public bool AutoApplyOnStartup { get; init; } = false;

    /// <summary>When true, minimizing or closing hides to system tray instead of taskbar.</summary>
    public bool MinimizeToTray { get; init; } = true;

    /// <summary>Name of the most recently applied profile, or null if none.</summary>
    public string? LastAppliedProfileName { get; init; }

    /// <summary>When true, brightness changes animate smoothly from current to target value.</summary>
    public bool SmoothTransition { get; init; } = false;

    /// <summary>Duration of smooth brightness transitions in milliseconds (100–2000).</summary>
    public int TransitionDurationMs { get; init; } = 500;

    /// <summary>When true, the application is registered to start with Windows.</summary>
    public bool StartWithWindows { get; init; } = false;

    /// <summary>When true, brightness values are refreshed from hardware when the window gains focus.</summary>
    public bool RefreshOnFocus { get; init; } = true;

    /// <summary>Name of the profile to apply automatically on GUI startup, or null for none.</summary>
    public string? DefaultStartupProfileName { get; init; }
}
