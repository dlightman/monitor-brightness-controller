namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents a release retrieved from the GitHub Releases API.
/// </summary>
/// <param name="TagName">The release tag (e.g., "v1.5.0").</param>
/// <param name="HtmlUrl">The URL to the release page on GitHub.</param>
public record GitHubRelease(string TagName, string HtmlUrl);
