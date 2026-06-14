using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Handles detection of the install location and copying the executable
/// to <c>%ProgramFiles%\MonitorBrightnessController\</c> via UAC elevation.
/// </summary>
public interface IApplicationInstaller
{
    /// <summary>
    /// Determines whether the current process is running from the install directory.
    /// </summary>
    /// <returns>True if the executable is located in Program Files.</returns>
    bool IsInstalledInProgramFiles();

    /// <summary>
    /// Copies the current executable to Program Files using UAC elevation.
    /// Returns the installed path on success.
    /// </summary>
    /// <returns>A success result containing the installed path, or a failure result on error.</returns>
    Result<string> InstallToProgramFiles();
}
