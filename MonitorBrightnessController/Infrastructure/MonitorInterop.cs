using System;
using System.Collections.Generic;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Infrastructure;

/// <summary>
/// DDC/CI interop layer. Wraps the Win32 Low-Level Monitor Configuration API
/// (<c>dxva2.dll</c>) and display enumeration (<c>user32.dll</c>) to discover physical
/// monitors and read/write their brightness via VCP code 0x10 (Luminance).
/// </summary>
/// <remarks>
/// This type performs real hardware communication and therefore cannot be meaningfully
/// unit-tested without an attached, DDC/CI-capable monitor. It is exercised via
/// integration tests or manual verification. All logic that does not require hardware
/// (index assignment, name fallback, support filtering) lives in the application layer.
/// </remarks>
public sealed class MonitorInterop : IMonitorInterop
{
    /// <inheritdoc />
    public IReadOnlyList<PhysicalMonitorInfo> EnumerateMonitors()
    {
        var hMonitors = new List<IntPtr>();

        // Collect every logical monitor handle. The callback returns true to continue
        // enumeration. We capture handles and process them after enumeration completes.
        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
        {
            hMonitors.Add(hMonitor);
            return true;
        };

        var results = new List<PhysicalMonitorInfo>();

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            // Enumeration failed outright; report no monitors rather than throwing.
            return results;
        }

        foreach (var hMonitor in hMonitors)
        {
            CollectPhysicalMonitors(hMonitor, results);
        }

        return results;
    }

    private static void CollectPhysicalMonitors(IntPtr hMonitor, List<PhysicalMonitorInfo> results)
    {
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
        {
            return;
        }

        var physicalMonitors = new NativeMethods.PHYSICAL_MONITOR[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
        {
            return;
        }

        // Resolve the device interface paths associated with this logical monitor. These
        // provide deterministic, stable identification across reboots. When the path
        // count does not line up with the physical monitor count we fall back to the
        // monitor description so that a handle is never dropped.
        var devicePaths = GetDevicePaths(hMonitor);

        for (int i = 0; i < physicalMonitors.Length; i++)
        {
            var pm = physicalMonitors[i];
            string devicePath = i < devicePaths.Count
                ? devicePaths[i]
                : pm.szPhysicalMonitorDescription ?? string.Empty;

            // A monitor supports DDC/CI if we can successfully read its brightness register.
            bool supportsDdcCi = NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                pm.hPhysicalMonitor,
                NativeMethods.VcpBrightness,
                out _,
                out _,
                out _);

            results.Add(new PhysicalMonitorInfo
            {
                DevicePath = devicePath,
                MonitorName = pm.szPhysicalMonitorDescription,
                PhysicalHandle = pm.hPhysicalMonitor,
                SupportsDdcCi = supportsDdcCi,
            });
        }
    }

    private static List<string> GetDevicePaths(IntPtr hMonitor)
    {
        var paths = new List<string>();

        var info = new NativeMethods.MONITORINFOEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            return paths;
        }

        // For the logical display (e.g. "\\.\DISPLAY1"), enumerate its attached monitor
        // device(s) requesting the device interface name (a stable device path).
        uint devIndex = 0;
        while (true)
        {
            var device = new NativeMethods.DISPLAY_DEVICE
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
            };

            if (!NativeMethods.EnumDisplayDevices(info.szDevice, devIndex, ref device, NativeMethods.EddGetDeviceInterfaceName))
            {
                break;
            }

            if (!string.IsNullOrEmpty(device.DeviceID))
            {
                paths.Add(device.DeviceID);
            }

            devIndex++;
        }

        return paths;
    }

    /// <inheritdoc />
    public Result<int> GetBrightness(IntPtr physicalMonitorHandle)
    {
        if (!NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                physicalMonitorHandle,
                NativeMethods.VcpBrightness,
                out _,
                out uint current,
                out uint maximum))
        {
            return Result<int>.Failure("Failed to read brightness from the monitor via DDC/CI.");
        }

        // VCP maximum is not guaranteed to be 100; scale the raw value to a 0-100 percentage.
        int percentage = maximum == 0
            ? (int)Math.Clamp(current, 0u, 100u)
            : (int)Math.Round(current * 100.0 / maximum);

        return Result<int>.Success(Math.Clamp(percentage, 0, 100));
    }

    /// <inheritdoc />
    public Result<Unit> SetBrightness(IntPtr physicalMonitorHandle, int value)
    {
        if (value < 0 || value > 100)
        {
            return Result<Unit>.Failure($"Brightness value '{value}' is out of the valid range [0, 100].");
        }

        // Determine the monitor's VCP maximum so the percentage can be scaled to the
        // raw register range. If the read fails, assume a 0-100 range.
        uint maximum = 100;
        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                physicalMonitorHandle,
                NativeMethods.VcpBrightness,
                out _,
                out _,
                out uint reportedMaximum)
            && reportedMaximum > 0)
        {
            maximum = reportedMaximum;
        }

        uint rawValue = (uint)Math.Round(value / 100.0 * maximum);

        if (!NativeMethods.SetVCPFeature(physicalMonitorHandle, NativeMethods.VcpBrightness, rawValue))
        {
            return Result<Unit>.Failure("Failed to set brightness on the monitor via DDC/CI.");
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <inheritdoc />
    public void ReleaseMonitors(IEnumerable<IntPtr> handles)
    {
        if (handles is null)
        {
            return;
        }

        foreach (var handle in handles)
        {
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            // DestroyPhysicalMonitors operates on an array; release handles one at a time
            // so a single invalid handle does not prevent releasing the rest.
            var single = new[]
            {
                new NativeMethods.PHYSICAL_MONITOR
                {
                    hPhysicalMonitor = handle,
                    szPhysicalMonitorDescription = string.Empty,
                },
            };

            NativeMethods.DestroyPhysicalMonitors(1, single);
        }
    }
}
