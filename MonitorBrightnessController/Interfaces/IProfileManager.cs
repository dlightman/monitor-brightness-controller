using System.Collections.Generic;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Manages named brightness profiles and their persistence. Enforces naming rules,
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
    /// Creates a new profile mapping monitor device paths to brightness values.
    /// Rejects invalid names, duplicate names (case-insensitive), and creation beyond the maximum count.
    /// </summary>
    /// <param name="name">The profile name (1–64 chars, <c>[a-zA-Z0-9_-]</c>).</param>
    /// <param name="monitorBrightnessMap">Map of device path to brightness value [0, 100].</param>
    /// <returns>A success result, or a failure result with a validation message.</returns>
    Result<Unit> CreateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap);

    /// <summary>
    /// Updates the monitor-to-brightness mapping for an existing profile.
    /// </summary>
    /// <param name="name">The name of the profile to update (case-insensitive match).</param>
    /// <param name="monitorBrightnessMap">The replacement map of device path to brightness value [0, 100].</param>
    /// <returns>A success result, or a failure result if the profile does not exist.</returns>
    Result<Unit> UpdateProfile(string name, IReadOnlyDictionary<string, int> monitorBrightnessMap);

    /// <summary>
    /// Deletes the profile with the given name (case-insensitive match).
    /// </summary>
    /// <param name="name">The name of the profile to delete.</param>
    /// <returns>A success result, or a failure result if the profile does not exist.</returns>
    Result<Unit> DeleteProfile(string name);

    /// <summary>
    /// Applies a profile's brightness values to all currently connected mapped monitors,
    /// skipping disconnected monitors, and records the applied profile as most recently used.
    /// </summary>
    /// <param name="name">The name of the profile to apply (case-insensitive match).</param>
    /// <param name="monitorService">The monitor service used to resolve monitors and apply brightness.</param>
    /// <returns>A success result if at least one connected monitor was updated, otherwise a failure result.</returns>
    Result<Unit> ApplyProfile(string name, IMonitorService monitorService);
}
