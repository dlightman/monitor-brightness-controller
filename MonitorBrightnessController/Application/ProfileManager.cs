using System;
using System.Collections.Generic;
using System.Linq;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Manages named brightness profiles and their persistence on top of an injected
/// <see cref="ISettingsStore"/>. Enforces the profile naming rules, the maximum profile
/// count, and case-insensitive name uniqueness. Profiles map monitors by device path so
/// that they remain stable across reboots and changes in enumeration order.
/// </summary>
public sealed class ProfileManager : IProfileManager
{
    /// <summary>The maximum number of profiles that may be stored.</summary>
    public const int MaxProfiles = 50;

    /// <summary>The minimum allowed profile name length (inclusive).</summary>
    public const int MinNameLength = 1;

    /// <summary>The maximum allowed profile name length (inclusive).</summary>
    public const int MaxNameLength = 64;

    private readonly ISettingsStore _settingsStore;

    /// <summary>
    /// Creates a new <see cref="ProfileManager"/> backed by the given settings store.
    /// </summary>
    /// <param name="settingsStore">The store used to load and persist profiles and settings.</param>
    public ProfileManager(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    /// <inheritdoc />
    public IReadOnlyList<Profile> GetAllProfiles()
    {
        return _settingsStore.Load().Profiles.ToList();
    }

    /// <inheritdoc />
    public Result<Profile> GetProfile(string name)
    {
        AppSettings settings = _settingsStore.Load();
        Profile? match = FindProfile(settings.Profiles, name);
        return match is null
            ? Result<Profile>.Failure($"Profile '{name}' not found")
            : Result<Profile>.Success(match);
    }

    /// <inheritdoc />
    public Result<Unit> CreateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap)
    {
        if (!IsValidProfileName(name))
        {
            return Result<Unit>.Failure(
                $"Invalid profile name '{name}': must be {MinNameLength}-{MaxNameLength} characters using only letters, digits, hyphens, and underscores");
        }

        if (monitorBrightnessMap is null)
        {
            return Result<Unit>.Failure("Profile brightness map must not be null.");
        }

        if (!TryValidateBrightnessMap(monitorBrightnessMap, out string? mapError))
        {
            return Result<Unit>.Failure(mapError!);
        }

        AppSettings settings = _settingsStore.Load();

        if (settings.Profiles.Count >= MaxProfiles)
        {
            return Result<Unit>.Failure($"Maximum of {MaxProfiles} profiles reached.");
        }

        if (FindProfile(settings.Profiles, name) is not null)
        {
            return Result<Unit>.Failure($"A profile named '{name}' already exists.");
        }

        var profiles = settings.Profiles.ToList();
        profiles.Add(new Profile
        {
            Name = name,
            MonitorBrightnessMap = new Dictionary<string, int>(monitorBrightnessMap),
        });

        return _settingsStore.Save(settings with { Profiles = profiles });
    }

    /// <inheritdoc />
    public Result<Unit> UpdateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap)
    {
        if (monitorBrightnessMap is null)
        {
            return Result<Unit>.Failure("Profile brightness map must not be null.");
        }

        if (!TryValidateBrightnessMap(monitorBrightnessMap, out string? mapError))
        {
            return Result<Unit>.Failure(mapError!);
        }

        AppSettings settings = _settingsStore.Load();
        var profiles = settings.Profiles.ToList();
        int slot = profiles.FindIndex(p => NameMatches(p.Name, name));
        if (slot < 0)
        {
            return Result<Unit>.Failure($"Profile '{name}' not found");
        }

        profiles[slot] = profiles[slot] with
        {
            MonitorBrightnessMap = new Dictionary<string, int>(monitorBrightnessMap),
        };

        return _settingsStore.Save(settings with { Profiles = profiles });
    }

    /// <inheritdoc />
    public Result<Unit> DeleteProfile(string name)
    {
        AppSettings settings = _settingsStore.Load();
        var profiles = settings.Profiles.ToList();
        int slot = profiles.FindIndex(p => NameMatches(p.Name, name));
        if (slot < 0)
        {
            return Result<Unit>.Failure($"Profile '{name}' not found");
        }

        profiles.RemoveAt(slot);
        return _settingsStore.Save(settings with { Profiles = profiles });
    }

    /// <inheritdoc />
    public Result<Unit> ApplyProfile(string name, IMonitorService monitorService)
    {
        if (monitorService is null)
        {
            throw new ArgumentNullException(nameof(monitorService));
        }

        AppSettings settings = _settingsStore.Load();
        Profile? profile = FindProfile(settings.Profiles, name);
        if (profile is null)
        {
            return Result<Unit>.Failure($"Profile '{name}' not found");
        }

        // Resolve currently connected monitors by device path.
        IReadOnlyList<MonitorState> connected = monitorService.DetectMonitors();
        var byDevicePath = new Dictionary<string, MonitorState>(StringComparer.Ordinal);
        foreach (MonitorState monitor in connected)
        {
            byDevicePath[monitor.DevicePath] = monitor;
        }

        int connectedCount = 0;
        var errors = new List<string>();

        foreach (KeyValuePair<string, int> entry in profile.MonitorBrightnessMap)
        {
            if (!byDevicePath.TryGetValue(entry.Key, out MonitorState? target))
            {
                // Monitor mapped by the profile is not currently connected: skip it (Req 4.5).
                continue;
            }

            connectedCount++;
            Result<Unit> set = monitorService.SetBrightness(target.MonitorIndex, entry.Value);
            if (!set.IsSuccess)
            {
                errors.Add(set.Error ?? $"Failed to set brightness on monitor {target.MonitorIndex}");
            }
        }

        // If none of the profile's mapped monitors are connected, the apply fails (Req 4.6).
        if (connectedCount == 0)
        {
            return Result<Unit>.Failure(
                $"Profile '{profile.Name}' has no mapped monitors that are currently connected.");
        }

        // At least one connected monitor was targeted: record the applied profile (Req 5.5).
        Result<Unit> saved = _settingsStore.Save(settings with { LastAppliedProfileName = profile.Name });
        if (!saved.IsSuccess)
        {
            return saved;
        }

        if (errors.Count > 0)
        {
            return Result<Unit>.Failure(string.Join("; ", errors));
        }

        return Result<Unit>.Success(Unit.Value);
    }

    // ---------------------------------------------------------------------
    // Pure helpers (no store dependency) — exercised directly by property tests.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Determines whether a candidate profile name is valid: it must have length between
    /// <see cref="MinNameLength"/> and <see cref="MaxNameLength"/> (inclusive) and consist
    /// solely of characters matching <c>[a-zA-Z0-9_-]</c>.
    /// </summary>
    /// <param name="name">The candidate profile name.</param>
    /// <returns>True when the name satisfies all rules; otherwise false.</returns>
    public static bool IsValidProfileName(string? name)
    {
        if (name is null)
        {
            return false;
        }

        if (name.Length < MinNameLength || name.Length > MaxNameLength)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!IsAllowedNameChar(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedNameChar(char c)
    {
        return (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '0' && c <= '9')
            || c == '_'
            || c == '-';
    }

    private static bool NameMatches(string a, string b)
    {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static Profile? FindProfile(IEnumerable<Profile> profiles, string name)
    {
        return profiles.FirstOrDefault(p => NameMatches(p.Name, name));
    }

    private static bool TryValidateBrightnessMap(IReadOnlyDictionary<string, int> map, out string? error)
    {
        foreach (KeyValuePair<string, int> entry in map)
        {
            if (entry.Value < MonitorService.MinBrightness || entry.Value > MonitorService.MaxBrightness)
            {
                error = $"Invalid brightness value '{entry.Value}' for monitor '{entry.Key}': must be integer "
                    + $"{MonitorService.MinBrightness}-{MonitorService.MaxBrightness}";
                return false;
            }
        }

        error = null;
        return true;
    }
}
