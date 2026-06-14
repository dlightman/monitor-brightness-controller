using System;
using System.Collections.Generic;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Interfaces;

/// <summary>
/// Wraps the Win32 Low-Level Monitor Configuration API (DDC/CI) via P/Invoke.
/// All native interop is isolated behind this interface for testability.
/// </summary>
public interface IMonitorInterop
{
    /// <summary>
    /// Enumerates all physical monitors currently attached to the system, reporting their
    /// device path, EDID name, native handle, and DDC/CI support status.
    /// </summary>
    /// <returns>The list of discovered physical monitors.</returns>
    IReadOnlyList<PhysicalMonitorInfo> EnumerateMonitors();

    /// <summary>
    /// Reads the current brightness (VCP code 0x10) for the specified physical monitor.
    /// </summary>
    /// <param name="physicalMonitorHandle">The native handle to the physical monitor.</param>
    /// <returns>A success result with the brightness value [0, 100], or a failure result on a DDC/CI communication error.</returns>
    Result<int> GetBrightness(IntPtr physicalMonitorHandle);

    /// <summary>
    /// Sets the brightness (VCP code 0x10) for the specified physical monitor.
    /// </summary>
    /// <param name="physicalMonitorHandle">The native handle to the physical monitor.</param>
    /// <param name="value">The brightness value to apply, in the range [0, 100].</param>
    /// <returns>A success result, or a failure result on a DDC/CI communication error.</returns>
    Result<Unit> SetBrightness(IntPtr physicalMonitorHandle, int value);

    /// <summary>
    /// Reads the current gamma (VCP code 0x12) for the specified physical monitor.
    /// </summary>
    /// <param name="physicalMonitorHandle">The native handle to the physical monitor.</param>
    /// <returns>A success result with the gamma value [0, 100], or a failure result on a DDC/CI communication error.</returns>
    Result<int> GetGamma(IntPtr physicalMonitorHandle);

    /// <summary>
    /// Sets the gamma (VCP code 0x12) for the specified physical monitor.
    /// </summary>
    /// <param name="physicalMonitorHandle">The native handle to the physical monitor.</param>
    /// <param name="value">The gamma value to apply, in the range [0, 100].</param>
    /// <returns>A success result, or a failure result on a DDC/CI communication error.</returns>
    Result<Unit> SetGamma(IntPtr physicalMonitorHandle, int value);

    /// <summary>
    /// Releases the native handles previously obtained for the supplied physical monitors.
    /// </summary>
    /// <param name="handles">The physical monitor handles to release.</param>
    void ReleaseMonitors(IEnumerable<IntPtr> handles);
}
