using System;
using Microsoft.Win32;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// Manages the application's presence in the Windows current-user startup (Run registry key).
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    /// <summary>
    /// Registers or unregisters the application for startup with Windows.
    /// </summary>
    /// <param name="enable">True to register, false to unregister.</param>
    public static void SetStartWithWindows(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;

        if (enable)
        {
            string exePath = Environment.ProcessPath ?? "";
            key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Checks whether the application is currently registered for startup with Windows.
    /// </summary>
    /// <returns>True if the registry value exists.</returns>
    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }
}
