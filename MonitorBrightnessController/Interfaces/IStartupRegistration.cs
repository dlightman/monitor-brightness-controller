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
    /// Reconciles the registry state on application startup.
    /// <list type="bullet">
    ///   <item>When <paramref name="startWithWindowsEnabled"/> is true: verifies the registry
    ///   entry exists and its path matches the current exe; overwrites if paths differ.</item>
    ///   <item>When <paramref name="startWithWindowsEnabled"/> is false: checks whether an
    ///   external registry entry exists (e.g. created by the installer). If found, signals that
    ///   the SettingsStore should be synced to true and updates the path if it differs.</item>
    /// </list>
    /// </summary>
    /// <param name="startWithWindowsEnabled">Whether the SettingsStore currently has StartWithWindows set to true.</param>
    /// <returns>
    /// A success result containing a <see cref="RegistrySyncResult"/> indicating whether settings
    /// need syncing and whether the path was updated, or a failure result if the registry key
    /// could not be accessed.
    /// </returns>
    Result<RegistrySyncResult> EnsureRegistration(bool startWithWindowsEnabled);

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
