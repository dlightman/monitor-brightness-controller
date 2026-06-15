using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Checks for available application updates by querying a remote release source.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Asynchronously checks whether a newer version of the application is available.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="UpdateCheckResult"/> indicating whether an update is available.</returns>
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default);
}
