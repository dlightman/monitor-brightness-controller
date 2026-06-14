using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// Handles detection of the install location and copying the executable
/// to <c>%ProgramFiles%\MonitorBrightnessController\</c> via UAC elevation.
/// </summary>
public class ApplicationInstaller : IApplicationInstaller
{
    private const string AppFolderName = "MonitorBrightnessController";
    private const string ExeFileName = "MonitorBrightnessController.exe";

    private static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolderName);

    private static string InstalledExePath =>
        Path.Combine(InstallDirectory, ExeFileName);

    /// <inheritdoc />
    public bool IsInstalledInProgramFiles()
    {
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentPath))
            return false;

        var normalizedCurrent = Path.GetFullPath(currentPath);
        var normalizedExpected = Path.GetFullPath(InstalledExePath);

        return string.Equals(normalizedCurrent, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Result<string> InstallToProgramFiles()
    {
        var sourcePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(sourcePath))
            return Result<string>.Failure("Could not determine the current executable path.");

        var targetDir = InstallDirectory;
        var targetPath = InstalledExePath;

        // Build a command that creates the directory if needed and copies the file.
        // Using /Y to overwrite without prompting.
        var command = $"mkdir \"{targetDir}\" 2>nul & copy /Y \"{sourcePath}\" \"{targetPath}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {command}",
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return Result<string>.Failure("Failed to start the elevated installer process.");

            process.WaitForExit();

            if (process.ExitCode != 0)
                return Result<string>.Failure($"File copy failed with exit code {process.ExitCode}.");

            // Verify the file was actually copied
            if (!File.Exists(targetPath))
                return Result<string>.Failure("The file copy appeared to succeed but the target file was not found.");

            return Result<string>.Success(targetPath);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return Result<string>.Failure("Install cancelled: the UAC elevation request was denied by the user.");
        }
        catch (Win32Exception ex)
        {
            return Result<string>.Failure($"Install failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"An unexpected error occurred during install: {ex.Message}");
        }
    }
}
