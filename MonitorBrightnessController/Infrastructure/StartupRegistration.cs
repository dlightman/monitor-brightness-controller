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
        try
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

                // Requirement 3.7: Overwrite existing entry if path differs (always write current exe path)
                runKey.SetValue(AppName, $"\"{exePath}\" --silent");
            }
            else
            {
                // Requirement 3.3: Tolerant of missing entry
                runKey.DeleteValue(AppName, throwOnMissingValue: false);
            }

            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            // Requirement 3.6 / 7.5: Return failure result on registry write failure
            return Result<Unit>.Failure($"Registry operation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result<RegistrySyncResult> EnsureRegistration(bool startWithWindowsEnabled)
    {
        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExePath))
        {
            return Result<RegistrySyncResult>.Failure("Could not determine the executable path.");
        }

        var expectedValue = $"\"{currentExePath}\" --silent";

        try
        {
            if (!startWithWindowsEnabled)
            {
                // Requirement 7.2: Check if an external registry entry exists (e.g. installer-created)
                using var readKey = _registryRoot.OpenSubKey(RunKey, writable: false);
                if (readKey is null)
                {
                    // Cannot open key — no sync needed (key doesn't exist or no access for read)
                    return Result<RegistrySyncResult>.Success(new RegistrySyncResult(SettingsNeedSync: false, PathWasUpdated: false));
                }

                var existingValue = readKey.GetValue(AppName) as string;
                if (existingValue is null)
                {
                    // No registry entry → nothing to sync
                    return Result<RegistrySyncResult>.Success(new RegistrySyncResult(SettingsNeedSync: false, PathWasUpdated: false));
                }

                // External entry detected — signal that SettingsStore needs sync to true
                // Requirement 7.4: Also check if path differs and update
                bool pathDiffers = !string.Equals(existingValue, expectedValue, StringComparison.OrdinalIgnoreCase);
                if (pathDiffers)
                {
                    // Need writable access to update the path
                    using var writeKey = _registryRoot.OpenSubKey(RunKey, writable: true);
                    if (writeKey is null)
                    {
                        return Result<RegistrySyncResult>.Failure("Unable to open the startup registry key for writing.");
                    }
                    writeKey.SetValue(AppName, expectedValue);
                }

                return Result<RegistrySyncResult>.Success(new RegistrySyncResult(SettingsNeedSync: true, PathWasUpdated: pathDiffers));
            }

            // startWithWindowsEnabled is true
            // Requirement 7.4: Compare registry entry value against current exe path and update if differs
            using var runKey = _registryRoot.OpenSubKey(RunKey, writable: true);
            if (runKey is null)
            {
                return Result<RegistrySyncResult>.Failure("Unable to open the startup registry key for writing.");
            }

            var currentValue = runKey.GetValue(AppName) as string;
            bool wasUpdated = false;

            if (currentValue is null || !string.Equals(currentValue, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                runKey.SetValue(AppName, expectedValue);
                wasUpdated = true;
            }

            return Result<RegistrySyncResult>.Success(new RegistrySyncResult(SettingsNeedSync: false, PathWasUpdated: wasUpdated));
        }
        catch (Exception ex)
        {
            // Requirement 7.5: Return failure result, preserve SettingsStore value unchanged
            return Result<RegistrySyncResult>.Failure($"Registry operation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Result<Unit> UpdateRegisteredPath(string newExePath)
    {
        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: true);
        if (runKey is null)
        {
            return Result<Unit>.Failure("Unable to open the startup registry key for writing.");
        }

        runKey.SetValue(AppName, $"\"{newExePath}\" --silent");
        return Result<Unit>.Success(Unit.Value);
    }

    /// <inheritdoc />
    public bool IsRegistered()
    {
        using var runKey = _registryRoot.OpenSubKey(RunKey, writable: false);
        return runKey?.GetValue(AppName) is not null;
    }
}
