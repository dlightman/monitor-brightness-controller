using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Serialization;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// JSON-backed implementation of <see cref="ISettingsStore"/> that persists application
/// state to <c>%LOCALAPPDATA%\MonitorBrightnessController\settings.json</c>.
/// </summary>
/// <remarks>
/// Loading is resilient: a missing or corrupted file yields default settings rather than
/// throwing. Both loading and saving retry once after a short delay when the file is locked
/// by another process. Saving returns a failure <see cref="Result{T}"/> (rather than throwing)
/// when the underlying write fails, e.g. due to a full disk or denied permissions.
/// </remarks>
public sealed class SettingsStore : ISettingsStore
{
    private const string AppFolderName = "MonitorBrightnessController";
    private const string SettingsFileName = "settings.json";
    private const int LockRetryDelayMs = 100;

    /// <summary>
    /// Shared serializer options used for both reading and writing settings. Exposed so that
    /// tests (and any other consumer) can round-trip <see cref="AppSettings"/> with identical
    /// configuration. Uses indented formatting and camelCase property names to match the
    /// documented settings schema (<c>profiles</c>, <c>monitorBrightnessMap</c>,
    /// <c>autoApplyOnStartup</c>, <c>lastAppliedProfileName</c>).
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;

    /// <summary>
    /// Creates a store that reads and writes the default settings file location under
    /// <c>%LOCALAPPDATA%</c>.
    /// </summary>
    public SettingsStore()
        : this(GetDefaultFilePath())
    {
    }

    /// <summary>
    /// Creates a store that reads and writes the supplied <paramref name="filePath"/>.
    /// Primarily intended for testing.
    /// </summary>
    /// <param name="filePath">Absolute path to the settings file.</param>
    public SettingsStore(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    /// <summary>Gets the absolute path to the settings file this store operates on.</summary>
    public string FilePath => _filePath;

    private static string GetDefaultFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, AppFolderName, SettingsFileName);
    }

    /// <inheritdoc />
    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            // Missing file: start from defaults.
            return new AppSettings();
        }

        string json;
        try
        {
            json = ReadAllTextWithRetry(_filePath);
        }
        catch (Exception ex)
        {
            // Unreadable (locked after retry, permissions, etc.): fall back to defaults.
            Trace.TraceWarning($"SettingsStore: failed to read '{_filePath}': {ex.Message}. Using default settings.");
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return settings ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            // Corrupted JSON: log a warning and fall back to defaults rather than crashing.
            Trace.TraceWarning($"SettingsStore: corrupted settings file '{_filePath}': {ex.Message}. Using default settings.");
            return new AppSettings();
        }
    }

    /// <inheritdoc />
    public Result<Unit> Save(AppSettings settings)
    {
        if (settings is null)
        {
            return Result<Unit>.Failure("Settings to save must not be null.");
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            WriteAllTextWithRetry(_filePath, json);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            // Disk full, permission denied, locked after retry, etc.: report failure, don't crash.
            Trace.TraceWarning($"SettingsStore: failed to save '{_filePath}': {ex.Message}.");
            return Result<Unit>.Failure($"Failed to save settings to '{_filePath}': {ex.Message}");
        }
    }

    private static string ReadAllTextWithRetry(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            // File may be transiently locked by another process: retry once after a short delay.
            Thread.Sleep(LockRetryDelayMs);
            return File.ReadAllText(path);
        }
    }

    private static void WriteAllTextWithRetry(string path, string contents)
    {
        try
        {
            File.WriteAllText(path, contents);
        }
        catch (IOException)
        {
            // File may be transiently locked by another process: retry once after a short delay.
            Thread.Sleep(LockRetryDelayMs);
            File.WriteAllText(path, contents);
        }
    }
}
