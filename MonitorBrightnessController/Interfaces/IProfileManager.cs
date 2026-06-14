using System.Collections.Generic;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Manages named brightness and gamma profiles and their persistence. Enforces naming rules,
/// the maximum profile count, and case-insensitive uniqueness.
/// </summary>
public interface IProfileManager
{
    /// <summary>
    /// Returns all stored profiles.
    /// </summary>
    IReadOnlyList<Profile> GetAllProfiles();

    /// <summary>
    /// Retrieves a profile by name using case-insensitive matching.
    /// </summary>
    /// <param name="name">The profile name to look up.</param>
    /// <returns>A success result with the profile, or a failure result if no profile matches.</returns>
    Result<Profile> GetProfile(string name);

    /// <summary>
    /// Creates a new profile mapping monitor device paths to brightness and optionally gamma values.
    /// Rejects invalid names, duplicate names (case-insensitive), and creation beyond the maximum count.
    /// </summary>
    /// <param name="name">The profile name (1–64 chars, <c>[a-zA-Z0-9_-]</c>).</param>
    /// <param name="monitorBrightnessMap">Map of device path to brightness value [0, 100].</param>
    /// <param name="gammaMap">
    /// Optional map of device path to gamma value [0, 100]. Pass <c>null</c> to create a
    /// brightness-only (legacy) profile that will not issue gamma commands when applied.
    /// </param>
    /// <returns>A success result, or a failure result with a validation message.</returns>
    Result<Unit> CreateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap,
        IReadOnlyDictionary<string, int>? gammaMap);

    /// <summary>
    /// Updates the monitor-to-brightness and gamma mappings for an existing profile.
    /// </summary>
    /// <param name="name">The name of the profile to update (case-insensitive match).</param>
    /// <param name="monitorBrightnessMap">The replacement map of device path to brightness value [0, 100].</param>
    /// <param name="gammaMap">
    /// Optional replacement map of device path to gamma value [0, 100]. Pass <c>null</c> to
    /// make the profile brightness-only (legacy); gamma commands will not be issued when applied.
    /// </param>
    /// <returns>A success result, or a failure result if the profile does not exist.</returns>
    Result<Unit> UpdateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap,
        IReadOnlyDictionary<string, int>? gammaMap);

    /// <summary>
    /// Deletes the profile with the given name (case-insensitive match).
    /// </summary>
    /// <param name="name">The name of the profile to delete.</param>
    /// <returns>A success result, or a failure result if the profile does not exist.</returns>
    Result<Unit> DeleteProfile(string name);

    /// <summary>
    /// Applies a profile's brightness and gamma values to all currently connected mapped monitors,
    /// skipping disconnected monitors, and records the applied profile as most recently used.
    /// Brightness and gamma are applied independently per monitor: a failure in one does not block the other.
    /// If the gamma map is null (legacy profile), only brightness commands are issued.
    /// </summary>
    /// <param name="name">The name of the profile to apply (case-insensitive match).</param>
    /// <param name="monitorService">The monitor service used to resolve monitors and apply brightness/gamma.</param>
    /// <returns>A success result if all operations succeeded, or a failure result with accumulated error details.</returns>
    Result<Unit> ApplyProfile(string name, IMonitorService monitorService);
}
