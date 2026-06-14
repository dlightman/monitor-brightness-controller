using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Manages registration of the application in the current-user Run registry key
/// so it can start automatically with Windows.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>
    /// Registers or unregisters the application for startup with Windows.
    /// </summary>
    /// <param name="enable">True to register, false to unregister.</param>
    /// <returns>A success result, or a failure result if the registry cannot be written.</returns>
    Result<Unit> SetStartWithWindows(bool enable);

    /// <summary>
    /// Checks whether the app is registered and whether the path matches the current exe.
    /// If mismatched or missing (while enabled), re-registers with the correct path.
    /// </summary>
    /// <param name="startWithWindowsEnabled">Whether the user has enabled start-with-Windows.</param>
    /// <returns>A success result, or a failure result if reconciliation fails.</returns>
    Result<Unit> EnsureRegistration(bool startWithWindowsEnabled);

    /// <summary>
    /// Updates the registry entry to point to a specific executable path (used by installer).
    /// </summary>
    /// <param name="newExePath">The new executable path to register.</param>
    /// <returns>A success result, or a failure result if the registry cannot be written.</returns>
    Result<Unit> UpdateRegisteredPath(string newExePath);

    /// <summary>
    /// Returns true if the registry entry currently exists.
    /// </summary>
    bool IsRegistered();
}
