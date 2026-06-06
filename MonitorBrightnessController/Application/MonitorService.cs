using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Orchestrates monitor detection, in-memory state tracking, and brightness operations.
/// Delegates all hardware interaction to an injected <see cref="IMonitorInterop"/> so that
/// the pure orchestration logic (index assignment, name fallback, value validation,
/// identifier resolution) can be exercised without real hardware.
/// </summary>
public sealed class MonitorService : IMonitorService
{
    /// <summary>The MCCS-standard minimum brightness value.</summary>
    public const int MinBrightness = 0;

    /// <summary>The MCCS-standard maximum brightness value.</summary>
    public const int MaxBrightness = 100;

    private readonly IMonitorInterop _interop;
    private readonly List<MonitorState> _monitors = new();

    /// <summary>
    /// Creates a new <see cref="MonitorService"/> backed by the given interop layer.
    /// </summary>
    /// <param name="interop">The DDC/CI interop layer used to enumerate and control monitors.</param>
    public MonitorService(IMonitorInterop interop)
    {
        _interop = interop ?? throw new ArgumentNullException(nameof(interop));
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitorState> DetectMonitors()
    {
        IReadOnlyList<PhysicalMonitorInfo> discovered = _interop.EnumerateMonitors();

        // Pure step: deterministic ordering, index assignment, name fallback, controllability.
        List<MonitorState> states = BuildMonitorStates(discovered).ToList();

        // Hardware step: read current brightness for controllable monitors.
        for (int i = 0; i < states.Count; i++)
        {
            MonitorState state = states[i];

            if (!state.IsControllable)
            {
                // Unsupported monitors have an unknown brightness and no controls (Req 1.3/1.4).
                states[i] = state with { CurrentBrightness = null };
                continue;
            }

            Result<int> read = _interop.GetBrightness(state.PhysicalHandle);
            states[i] = read.IsSuccess
                ? state with { CurrentBrightness = read.Value, ErrorMessage = null }
                : state with { CurrentBrightness = null, ErrorMessage = read.Error };
        }

        _monitors.Clear();
        _monitors.AddRange(states);
        return _monitors.AsReadOnly();
    }

    /// <inheritdoc />
    public Result<Unit> SetBrightness(int monitorIndex, int brightnessValue)
    {
        if (!IsValidBrightness(brightnessValue))
        {
            return Result<Unit>.Failure(
                $"Invalid brightness value '{brightnessValue}': must be integer {MinBrightness}-{MaxBrightness}");
        }

        int slot = _monitors.FindIndex(m => m.MonitorIndex == monitorIndex);
        if (slot < 0)
        {
            return Result<Unit>.Failure($"Monitor '{monitorIndex}' not found");
        }

        MonitorState target = _monitors[slot];
        Result<Unit> result = _interop.SetBrightness(target.PhysicalHandle, brightnessValue);

        _monitors[slot] = result.IsSuccess
            ? target with { CurrentBrightness = brightnessValue, ErrorMessage = null }
            : target with { ErrorMessage = result.Error };

        return result;
    }

    /// <inheritdoc />
    public Result<int> GetBrightness(int monitorIndex)
    {
        int slot = _monitors.FindIndex(m => m.MonitorIndex == monitorIndex);
        if (slot < 0)
        {
            return Result<int>.Failure($"Monitor '{monitorIndex}' not found");
        }

        MonitorState target = _monitors[slot];
        Result<int> result = _interop.GetBrightness(target.PhysicalHandle);

        _monitors[slot] = result.IsSuccess
            ? target with { CurrentBrightness = result.Value, ErrorMessage = null }
            : target with { CurrentBrightness = null, ErrorMessage = result.Error };

        return result;
    }

    /// <inheritdoc />
    public MonitorState? FindMonitor(string identifier) => FindMonitor(_monitors, identifier);

    // ---------------------------------------------------------------------
    // Pure helpers (no hardware dependency) — used directly by property tests.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Produces the deterministic per-monitor state for a set of enumerated physical monitors:
    /// orders by device path (ordinal), assigns indices starting at 1, applies the name fallback,
    /// and sets controllability from DDC/CI support. Brightness is left unknown (null) since it
    /// requires hardware communication.
    /// </summary>
    /// <param name="monitors">The raw enumerated monitors.</param>
    /// <returns>The ordered, indexed monitor states.</returns>
    public static IReadOnlyList<MonitorState> BuildMonitorStates(IReadOnlyList<PhysicalMonitorInfo> monitors)
    {
        if (monitors is null)
        {
            throw new ArgumentNullException(nameof(monitors));
        }

        return monitors
            .OrderBy(m => m.DevicePath, StringComparer.Ordinal)
            .Select((m, ordinal) =>
            {
                int index = ordinal + 1;
                return new MonitorState
                {
                    MonitorIndex = index,
                    MonitorName = ResolveMonitorName(m.MonitorName, index),
                    DevicePath = m.DevicePath,
                    PhysicalHandle = m.PhysicalHandle,
                    IsControllable = m.SupportsDdcCi,
                    CurrentBrightness = null,
                    ErrorMessage = null,
                };
            })
            .ToList();
    }

    /// <summary>
    /// Resolves the display name for a monitor, falling back to "Monitor N" when the
    /// EDID-reported name is null, empty, or whitespace.
    /// </summary>
    /// <param name="rawName">The EDID-reported monitor name, which may be null/empty/whitespace.</param>
    /// <param name="index">The monitor index N used in the fallback name.</param>
    /// <returns>The resolved display name.</returns>
    public static string ResolveMonitorName(string? rawName, int index)
    {
        return string.IsNullOrWhiteSpace(rawName) ? $"Monitor {index}" : rawName;
    }

    /// <summary>
    /// Determines whether an integer brightness value is within the valid range [0, 100].
    /// </summary>
    /// <param name="value">The brightness value to check.</param>
    /// <returns>True when the value is in range; otherwise false.</returns>
    public static bool IsValidBrightness(int value) => value is >= MinBrightness and <= MaxBrightness;

    /// <summary>
    /// Attempts to parse a string as an integer brightness value in the range [0, 100].
    /// Rejects non-numeric input, floats, values out of range, and surrounding noise.
    /// </summary>
    /// <param name="input">The candidate brightness string.</param>
    /// <param name="value">The parsed value when the input is valid; otherwise 0.</param>
    /// <returns>True when the input is a valid brightness string; otherwise false.</returns>
    public static bool TryParseBrightness(string? input, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!int.TryParse(input.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
        {
            return false;
        }

        if (!IsValidBrightness(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// Resolves a monitor from a set by identifier: a numeric string matches by
    /// <see cref="MonitorState.MonitorIndex"/>, otherwise the identifier is matched
    /// case-insensitively against <see cref="MonitorState.MonitorName"/>.
    /// </summary>
    /// <param name="monitors">The candidate monitors.</param>
    /// <param name="identifier">A numeric index string or a monitor name.</param>
    /// <returns>The matching monitor, or null when none matches.</returns>
    public static MonitorState? FindMonitor(IReadOnlyList<MonitorState> monitors, string identifier)
    {
        if (monitors is null || string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        string trimmed = identifier.Trim();

        if (int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index))
        {
            return monitors.FirstOrDefault(m => m.MonitorIndex == index);
        }

        return monitors.FirstOrDefault(
            m => string.Equals(m.MonitorName, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
