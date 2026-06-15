using System.IO;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Pure helper for building Windows shortcut (.lnk) parameters from a profile name
/// and executable path. Separates the testable argument-building logic from
/// the COM-dependent shortcut creation.
/// </summary>
public static class ShortcutHelper
{
    /// <summary>
    /// Builds the command-line arguments string for a profile shortcut.
    /// Format: <c>--profile {profileName}</c>
    /// </summary>
    /// <param name="profileName">The profile name to embed in the arguments.</param>
    /// <returns>The formatted arguments string.</returns>
    public static string BuildArguments(string profileName)
        => $"--profile {profileName}";

    /// <summary>
    /// Gets the target path for the shortcut (the application executable path).
    /// </summary>
    /// <param name="executablePath">The full path to the application executable.</param>
    /// <returns>The executable path to set as the shortcut target.</returns>
    public static string GetTargetPath(string executablePath)
        => executablePath;

    /// <summary>
    /// Gets the working directory for the shortcut (the executable's parent folder).
    /// </summary>
    /// <param name="executablePath">The full path to the application executable.</param>
    /// <returns>The parent directory of the executable.</returns>
    public static string GetWorkingDirectory(string executablePath)
        => Path.GetDirectoryName(executablePath) ?? "";

    /// <summary>
    /// Builds the default shortcut filename for a given profile.
    /// Format: <c>Brightness - {profileName}.lnk</c>
    /// </summary>
    /// <param name="profileName">The profile name.</param>
    /// <returns>The default shortcut filename.</returns>
    public static string BuildDefaultFileName(string profileName)
        => $"Brightness - {profileName}.lnk";
}
