using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Handles JSON serialization and deserialization of application state to and from
/// <c>%LOCALAPPDATA%\MonitorBrightnessController\settings.json</c>.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads application settings from disk. If the file is missing or corrupted,
    /// returns a default <see cref="AppSettings"/> instance rather than throwing.
    /// </summary>
    /// <returns>The loaded or default application settings.</returns>
    AppSettings Load();

    /// <summary>
    /// Persists the supplied application settings to disk.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <returns>A success result, or a failure result on an I/O error.</returns>
    Result<Unit> Save(AppSettings settings);
}
