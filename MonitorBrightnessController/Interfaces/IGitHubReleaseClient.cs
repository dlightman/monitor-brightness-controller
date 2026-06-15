using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Retrieves release information from the GitHub Releases API.
/// </summary>
public interface IGitHubReleaseClient
{
    /// <summary>
    /// Asynchronously fetches the latest published release from GitHub.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GitHubRelease"/> containing the tag name and release URL,
    /// or <c>null</c> if the release could not be retrieved.
    /// </returns>
    Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken = default);
}
