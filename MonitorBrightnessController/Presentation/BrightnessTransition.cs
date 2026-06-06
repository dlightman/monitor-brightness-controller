using System;
using System.Threading;
using System.Threading.Tasks;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Presentation;

/// <summary>
/// Provides smooth brightness transition by stepping gradually from one value to another
/// over a configurable duration.
/// </summary>
public static class BrightnessTransition
{
    /// <summary>
    /// Transitions brightness from <paramref name="fromValue"/> to <paramref name="toValue"/>
    /// over <paramref name="durationMs"/> milliseconds, calling <paramref name="applyStep"/>
    /// at each intermediate step.
    /// </summary>
    /// <param name="applyStep">Callback that applies a brightness value to hardware.</param>
    /// <param name="fromValue">The starting brightness value.</param>
    /// <param name="toValue">The target brightness value.</param>
    /// <param name="durationMs">Total transition duration in milliseconds.</param>
    /// <param name="ct">Cancellation token to abort the transition early.</param>
    /// <returns>A task that completes when the transition finishes or is cancelled.</returns>
    public static async Task TransitionAsync(
        Func<int, Result<Unit>> applyStep,
        int fromValue,
        int toValue,
        int durationMs,
        CancellationToken ct = default)
    {
        // If cancelled, duration is zero, or no change needed, just apply the final value.
        if (ct.IsCancellationRequested || durationMs <= 0 || fromValue == toValue)
        {
            applyStep(toValue);
            return;
        }

        int distance = Math.Abs(toValue - fromValue);

        // Determine step size: 2 for small moves, 3 for medium, 5 for large
        int stepSize = distance switch
        {
            <= 10 => 2,
            <= 30 => 3,
            _ => 5
        };

        int totalSteps = Math.Max(1, distance / stepSize);
        int delayPerStep = Math.Max(1, durationMs / totalSteps);
        int direction = toValue > fromValue ? 1 : -1;

        int current = fromValue;
        for (int i = 0; i < totalSteps - 1; i++)
        {
            if (ct.IsCancellationRequested)
            {
                applyStep(toValue);
                return;
            }

            current += direction * stepSize;

            // Clamp to not overshoot
            if ((direction > 0 && current > toValue) || (direction < 0 && current < toValue))
            {
                current = toValue;
            }

            applyStep(current);

            try
            {
                await Task.Delay(delayPerStep, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                applyStep(toValue);
                return;
            }
        }

        // Always apply the exact final value
        applyStep(toValue);
    }
}
