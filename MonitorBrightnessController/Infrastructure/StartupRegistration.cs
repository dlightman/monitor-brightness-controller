using System;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// Manages the application's presence in the Windows current-user startup (Run registry key).
/// </summary>
public class StartupRegistration : IStartupRegistration
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MonitorBrightnessController";

    private readonly IRegistryKeyWrapper _registryRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="StartupRegistration"/>.
    /// </summary>
    /// <param name="registryRoot">
    /// A wrapper representing the Registry.CurrentUser root key.
    /// </param>
    public StartupRegistration(IRegistryKeyWrapper registryRoot)
    {
        _registryRoot = registryRoot ?? throw new ArgumentNullException(nameof(registryRoot));
    }

    /// <inheritdoc />
    public Result<Unit> SetStartWithWindows(bool enable)
    {
        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: enable);
        if (runKey is null)
        {
            return Result<Unit>.Failure("Unable to open the startup registry key for writing.");
        }

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return Result<Unit>.Failure("Could not determine the executable path.");
            }

            runKey.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            runKey.DeleteValue(AppName, throwOnMissingValue: false);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <inheritdoc />
    public Result<Unit> EnsureRegistration(bool startWithWindowsEnabled)
    {
        if (!startWithWindowsEnabled)
        {
            return Result<Unit>.Success(Unit.Value);
        }

        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: true);
        if (runKey is null)
        {
            return Result<Unit>.Failure("Unable to open the startup registry key for writing.");
        }

        var existingValue = runKey.GetValue(AppName) as string;

        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExePath))
        {
            return Result<Unit>.Failure("Could not determine the executable path.");
        }

        var quotedPath = $"\"{currentExePath}\"";

        if (existingValue is null)
        {
            runKey.SetValue(AppName, quotedPath);
        }
        else if (!string.Equals(existingValue, quotedPath, StringComparison.OrdinalIgnoreCase))
        {
            runKey.SetValue(AppName, quotedPath);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <inheritdoc />
    public Result<Unit> UpdateRegisteredPath(string newExePath)
    {
        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: true);
        if (runKey is null)
        {
            return Result<Unit>.Failure("Unable to open the startup registry key for writing.");
        }

        runKey.SetValue(AppName, $"\"{newExePath}\"");
        return Result<Unit>.Success(Unit.Value);
    }

    /// <inheritdoc />
    public bool IsRegistered()
    {
        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: false);
        return runKey?.GetValue(AppName) is not null;
    }
}
