using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Identifies a unique transition context: a specific setting on a specific monitor.
/// </summary>
public enum SettingType
{
    Brightness,
    Gamma
}

/// <summary>
/// Coordinates smooth value transitions per monitor per setting type. Each (monitorIndex, settingType)
/// pair runs independently — a brightness transition on monitor 1 does not block a gamma transition
/// on monitor 1. Cancelling a transition (e.g. when a new target is requested) is cooperative via
/// CancellationToken.
/// </summary>
public sealed class TransitionCoordinator : IDisposable
{
    private readonly TransitionRunner _runner = new();

    /// <summary>
    /// Tracks the active CancellationTokenSource for each (monitorIndex, settingType) pair.
    /// </summary>
    private readonly ConcurrentDictionary<(int MonitorIndex, SettingType Setting), CancellationTokenSource> _activeTransitions = new();

    /// <summary>
    /// Tracks the last successfully applied value for each (monitorIndex, settingType) pair.
    /// Used to start new transitions from the correct intermediate value after cancellation.
    /// </summary>
    private readonly ConcurrentDictionary<(int MonitorIndex, SettingType Setting), int> _lastAppliedValues = new();

    /// <summary>
    /// Gets the last successfully applied value for the given monitor and setting,
    /// or <paramref name="fallback"/> if no value has been tracked yet.
    /// </summary>
    public int GetLastAppliedValue(int monitorIndex, SettingType setting, int fallback)
    {
        return _lastAppliedValues.TryGetValue((monitorIndex, setting), out int value)
            ? value
            : fallback;
    }

    /// <summary>
    /// Sets the last applied value for the given monitor and setting.
    /// Used to seed initial values when a monitor is first detected.
    /// </summary>
    public void SetLastAppliedValue(int monitorIndex, SettingType setting, int value)
    {
        _lastAppliedValues[(monitorIndex, setting)] = value;
    }

    /// <summary>
    /// Starts a smooth transition for the specified monitor and setting.
    /// If a transition is already in progress for the same (monitorIndex, settingType),
    /// it is cancelled first and the new transition starts from the last applied value.
    /// </summary>
    /// <param name="monitorIndex">The monitor index.</param>
    /// <param name="setting">The setting type (Brightness or Gamma).</param>
    /// <param name="targetValue">The target value to transition to.</param>
    /// <param name="durationMs">The transition duration in milliseconds.</param>
    /// <param name="applyStep">Delegate that applies a single value to hardware via DDC/CI.</param>
    /// <param name="onCompleted">
    /// Optional callback invoked on the calling context when the transition completes or fails.
    /// Receives the last successfully applied value and any error message (null on success).
    /// </param>
    public void StartTransition(
        int monitorIndex,
        SettingType setting,
        int targetValue,
        int durationMs,
        Func<int, Result<Unit>> applyStep,
        Action<int, string?>? onCompleted = null)
    {
        var key = (monitorIndex, setting);

        // Cancel any in-progress transition for this key
        CancelTransition(monitorIndex, setting);

        // Determine starting point: last applied value (or target if unknown)
        int fromValue = GetLastAppliedValue(monitorIndex, setting, targetValue);

        // If we're already at target, no transition needed
        if (fromValue == targetValue)
        {
            onCompleted?.Invoke(targetValue, null);
            return;
        }

        // Create new CTS for this transition
        var cts = new CancellationTokenSource();
        _activeTransitions[key] = cts;

        // Fire-and-forget the transition
        _ = RunTransitionInternalAsync(key, fromValue, targetValue, durationMs, applyStep, cts.Token, onCompleted);
    }

    /// <summary>
    /// Applies a value directly (no transition) for the specified monitor and setting.
    /// Cancels any in-progress transition first.
    /// </summary>
    /// <param name="monitorIndex">The monitor index.</param>
    /// <param name="setting">The setting type (Brightness or Gamma).</param>
    /// <param name="value">The value to apply directly.</param>
    /// <param name="applyStep">Delegate that applies the value to hardware via DDC/CI.</param>
    /// <returns>The result of the DDC/CI call.</returns>
    public Result<Unit> ApplyDirect(
        int monitorIndex,
        SettingType setting,
        int value,
        Func<int, Result<Unit>> applyStep)
    {
        // Cancel any in-progress transition for this key
        CancelTransition(monitorIndex, setting);

        var result = applyStep(value);
        if (result.IsSuccess)
        {
            _lastAppliedValues[(monitorIndex, setting)] = value;
        }

        return result;
    }

    /// <summary>
    /// Cancels any in-progress transition for the specified monitor and setting.
    /// </summary>
    public void CancelTransition(int monitorIndex, SettingType setting)
    {
        var key = (monitorIndex, setting);
        if (_activeTransitions.TryRemove(key, out var existingCts))
        {
            existingCts.Cancel();
            existingCts.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var kvp in _activeTransitions)
        {
            kvp.Value.Cancel();
            kvp.Value.Dispose();
        }
        _activeTransitions.Clear();
    }

    private async Task RunTransitionInternalAsync(
        (int MonitorIndex, SettingType Setting) key,
        int from,
        int to,
        int durationMs,
        Func<int, Result<Unit>> applyStep,
        CancellationToken ct,
        Action<int, string?>? onCompleted)
    {
        try
        {
            // Wrap the applyStep to track last applied value
            Func<int, Result<Unit>> trackingApplyStep = (int value) =>
            {
                var result = applyStep(value);
                if (result.IsSuccess)
                {
                    _lastAppliedValues[key] = value;
                }
                return result;
            };

            var result = await _runner.RunTransitionAsync(from, to, durationMs, trackingApplyStep, ct)
                .ConfigureAwait(false);

            // Clean up from active transitions dict
            _activeTransitions.TryRemove(key, out _);

            if (result.IsSuccess)
            {
                onCompleted?.Invoke(result.Value, null);
            }
            else
            {
                // DDC/CI failure during transition: retain last applied value, surface error
                int lastApplied = GetLastAppliedValue(key.MonitorIndex, key.Setting, from);
                onCompleted?.Invoke(lastApplied, result.Error);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Unexpected error: clean up and report
            _activeTransitions.TryRemove(key, out _);
            int lastApplied = GetLastAppliedValue(key.MonitorIndex, key.Setting, from);
            onCompleted?.Invoke(lastApplied, ex.Message);
        }
    }
}
