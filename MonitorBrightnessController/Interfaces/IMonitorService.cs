using System.Collections.Generic;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Orchestrates monitor detection, in-memory state tracking, and brightness operations.
/// Sits between the presentation/CLI layers and the DDC/CI interop layer.
/// </summary>
public interface IMonitorService
{
    /// <summary>
    /// Enumerates monitors via the interop layer, assigns stable indices based on sorted
    /// device paths (starting at 1), applies the monitor name fallback, and returns the
    /// resulting per-monitor state.
    /// </summary>
    /// <returns>The list of detected monitor states.</returns>
    IReadOnlyList<MonitorState> DetectMonitors();

    /// <summary>
    /// Validates the brightness value (0–100) and applies it to the monitor at the given index.
    /// </summary>
    /// <param name="monitorIndex">The stable index of the target monitor.</param>
    /// <param name="brightnessValue">The brightness value to apply, in the range [0, 100].</param>
    /// <returns>A success result, or a failure result on validation or DDC/CI errors.</returns>
    Result<Unit> SetBrightness(int monitorIndex, int brightnessValue);

    /// <summary>
    /// Reads the current brightness for the monitor at the given index and updates in-memory state.
    /// </summary>
    /// <param name="monitorIndex">The stable index of the target monitor.</param>
    /// <returns>A success result with the brightness value, or a failure result on a DDC/CI error.</returns>
    Result<int> GetBrightness(int monitorIndex);

    /// <summary>
    /// Validates the gamma value (0–100) and applies it to the monitor at the given index.
    /// </summary>
    /// <param name="monitorIndex">The stable index of the target monitor.</param>
    /// <param name="gammaValue">The gamma value to apply, in the range [0, 100].</param>
    /// <returns>A success result, or a failure result on validation or DDC/CI errors.</returns>
    Result<Unit> SetGamma(int monitorIndex, int gammaValue);

    /// <summary>
    /// Reads the current gamma for the monitor at the given index and updates in-memory state.
    /// </summary>
    /// <param name="monitorIndex">The stable index of the target monitor.</param>
    /// <returns>A success result with the gamma value, or a failure result on a DDC/CI error.</returns>
    Result<int> GetGamma(int monitorIndex);

    /// <summary>
    /// Resolves a monitor by its identifier, which may be a numeric index or a
    /// case-insensitive monitor name.
    /// </summary>
    /// <param name="identifier">A monitor index (as a numeric string) or monitor name.</param>
    /// <returns>The matching monitor state, or <c>null</c> if no monitor matches.</returns>
    MonitorState? FindMonitor(string identifier);
}
