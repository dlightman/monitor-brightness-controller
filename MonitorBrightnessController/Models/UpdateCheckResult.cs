namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents the result of checking for application updates.
/// </summary>
/// <param name="IsUpdateAvailable">Whether a newer version is available.</param>
/// <param name="LatestVersion">The latest version string (e.g., "1.5.0"), or null if unavailable.</param>
/// <param name="ReleaseUrl">The URL to the GitHub release page, or null if unavailable.</param>
public record UpdateCheckResult(bool IsUpdateAvailable, string? LatestVersion, string? ReleaseUrl);
