using System;

namespace MonitorBrightnessController.Models;

/// <summary>
/// Represents a physical monitor discovered by the DDC/CI interop layer during enumeration.
/// This is the raw, hardware-level description produced by <c>IMonitorInterop.EnumerateMonitors</c>
/// before it is mapped into application-level <see cref="MonitorState"/> instances.
/// </summary>
public record PhysicalMonitorInfo
{
    /// <summary>
    /// The Windows device path uniquely identifying the monitor. Used for deterministic
    /// ordering and stable identification across reboots.
    /// </summary>
    public string DevicePath { get; init; } = string.Empty;

    /// <summary>
    /// The human-readable monitor description reported via the
    /// <c>PHYSICAL_MONITOR.szPhysicalMonitorDescription</c> (EDID) field. May be null,
    /// empty, or whitespace, in which case callers apply a "Monitor N" fallback.
    /// </summary>
    public string? MonitorName { get; init; }

    /// <summary>
    /// The native handle to the physical monitor, used for subsequent VCP feature calls.
    /// </summary>
    public IntPtr PhysicalHandle { get; init; }

    /// <summary>
    /// Indicates whether the monitor supports DDC/CI communication and is therefore controllable.
    /// </summary>
    public bool SupportsDdcCi { get; init; }
}
