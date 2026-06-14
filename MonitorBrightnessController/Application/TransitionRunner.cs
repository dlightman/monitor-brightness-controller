using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Application;

/// <summary>
/// Executes smooth value transitions by calculating intermediate integer steps
/// distributed evenly over a specified duration. Shared by brightness and gamma transitions.
/// </summary>
internal sealed class TransitionRunner
{
    /// <summary>Approximate frame interval in milliseconds (~60 fps).</summary>
    private const int FrameIntervalMs = 16;

    /// <summary>
    /// Runs a smooth transition from <paramref name="from"/> to <paramref name="to"/> over
    /// <paramref name="durationMs"/> milliseconds, applying each intermediate value via
    /// <paramref name="applyStep"/>.
    /// </summary>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The target value.</param>
    /// <param name="durationMs">Total transition duration in milliseconds.</param>
    /// <param name="applyStep">Delegate that applies a single intermediate value to hardware.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> containing the last successfully applied value.
    /// On full completion this equals <paramref name="to"/>.
    /// On cancellation or failure it reflects the most recent value that was applied successfully.
    /// </returns>
    public async Task<Result<int>> RunTransitionAsync(
        int from,
        int to,
        int durationMs,
        Func<int, Result<Unit>> applyStep,
        CancellationToken ct)
    {
        // Edge case: no change needed
        if (from == to)
        {
            return Result<int>.Success(from);
        }

        // Calculate step count based on duration and frame rate
        int stepCount = Math.Max(1, durationMs / FrameIntervalMs);
        int delayPerStep = durationMs / stepCount;

        int lastApplied = from;

        for (int i = 1; i <= stepCount; i++)
        {
            // Check for cancellation before applying
            if (ct.IsCancellationRequested)
            {
                return Result<int>.Success(lastApplied);
            }

            // Calculate the intermediate value using linear interpolation.
            // The final iteration (i == stepCount) always produces exactly 'to'.
            int value = (i == stepCount)
                ? to
                : from + (int)Math.Round((double)(to - from) * i / stepCount);

            // Apply the step
            var result = applyStep(value);
            if (!result.IsSuccess)
            {
                return Result<int>.Failure(result.Error ?? "Transition step failed");
            }

            lastApplied = value;

            // Don't delay after the final step
            if (i < stepCount)
            {
                try
                {
                    await Task.Delay(delayPerStep, ct);
                }
                catch (OperationCanceledException)
                {
                    return Result<int>.Success(lastApplied);
                }
            }
        }

        return Result<int>.Success(lastApplied);
    }
}
