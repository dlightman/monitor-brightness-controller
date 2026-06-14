using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 16: Smooth transition interpolation reaches target
// Feature: gamma-control, Property 17: Transition cancellation starts from last applied value

/// <summary>
/// Custom arbitraries for smooth transition property tests.
/// </summary>
public static class TransitionArbitraries
{
    /// <summary>
    /// Generates (from, to, durationMs) tuples where from and to are in [0, 100]
    /// and duration is in [100, 200] to keep tests fast while ensuring predictable step counts.
    /// </summary>
    public static Arbitrary<(int From, int To, int DurationMs)> TransitionParams()
    {
        var gen =
            from fromVal in Gen.Choose(0, 100)
            from toVal in Gen.Choose(0, 100)
            from duration in Gen.Choose(100, 200)
            select (fromVal, toVal, duration);

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates (from, to, cancelAfterSteps) tuples for cancellation tests.
    /// from and to are in [0, 100] with from != to, and cancelAfterSteps is in [1, 5]
    /// (small values ensure cancellation happens before completion with short durations).
    /// </summary>
    public static Arbitrary<(int From, int To, int CancelAfterSteps)> CancellationParams()
    {
        var gen =
            from fromVal in Gen.Choose(0, 100)
            from toVal in Gen.Choose(0, 100).Where(t => true) // allow same for filtering below
            from cancelAfter in Gen.Choose(1, 5)
            where fromVal != toVal
            select (fromVal, toVal, cancelAfter);

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for TransitionRunner smooth transition behavior.
/// Tests that transitions reach their target value and that cancellation
/// preserves the last applied intermediate value.
/// </summary>
public class TransitionProperties
{
    /// <summary>
    /// Property 16: For any starting gamma value, target gamma value (both in [0, 100]),
    /// and duration in [100, 200] ms, the smooth transition produces a sequence of
    /// intermediate values where the final applied value equals the target.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.1**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TransitionArbitraries) })]
    public void SmoothTransition_FinalAppliedValue_EqualsTarget((int From, int To, int DurationMs) param)
    {
        var runner = new TransitionRunner();
        var appliedValues = new List<int>();

        Func<int, Result<Unit>> applyStep = value =>
        {
            appliedValues.Add(value);
            return Result<Unit>.Success(Unit.Value);
        };

        var result = runner.RunTransitionAsync(
            param.From, param.To, param.DurationMs, applyStep, CancellationToken.None)
            .GetAwaiter().GetResult();

        result.IsSuccess.Should().BeTrue(
            "transition from {0} to {1} over {2}ms should complete successfully",
            param.From, param.To, param.DurationMs);

        result.Value.Should().Be(param.To,
            "the Result<int>.Value should equal the target value {0}", param.To);

        if (param.From != param.To)
        {
            appliedValues.Should().NotBeEmpty(
                "a transition where from != to should apply at least one intermediate value");

            appliedValues[^1].Should().Be(param.To,
                "the last value passed to applyStep should equal the target {0}", param.To);
        }
    }

    /// <summary>
    /// Property 17: For any in-progress gamma transition that is cancelled after N steps,
    /// the Result<int>.Value equals the last applied intermediate value,
    /// which can serve as the starting point for a new transition.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 4.3**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TransitionArbitraries) })]
    public void TransitionCancellation_ReturnsLastAppliedValue((int From, int To, int CancelAfterSteps) param)
    {
        // Skip cases where from == to (no steps are generated)
        if (param.From == param.To)
            return;

        var runner = new TransitionRunner();
        var appliedValues = new List<int>();
        var cts = new CancellationTokenSource();
        int stepCount = 0;

        Func<int, Result<Unit>> applyStep = value =>
        {
            appliedValues.Add(value);
            stepCount++;

            // Cancel after N steps to simulate user interrupting with a new target
            if (stepCount >= param.CancelAfterSteps)
            {
                cts.Cancel();
            }

            return Result<Unit>.Success(Unit.Value);
        };

        // Use a duration that generates enough steps for cancellation to be meaningful
        // With ~16ms per frame, 200ms gives ~12 steps
        int durationMs = 200;

        var result = runner.RunTransitionAsync(
            param.From, param.To, durationMs, applyStep, cts.Token)
            .GetAwaiter().GetResult();

        result.IsSuccess.Should().BeTrue(
            "a cancelled transition should still return a success result with the last applied value");

        appliedValues.Should().NotBeEmpty(
            "at least one value should have been applied before or at the point of cancellation");

        // The result value should equal the last value that was actually applied
        int lastApplied = appliedValues[^1];
        result.Value.Should().Be(lastApplied,
            "Result.Value ({0}) should equal the last applied intermediate value ({1}), " +
            "so a new transition can start from this value",
            result.Value, lastApplied);

        // Verify the cancellation happened before reaching target
        // (unless cancelAfterSteps is large enough to complete the transition)
        if (appliedValues.Count < GetExpectedTotalSteps(durationMs))
        {
            lastApplied.Should().NotBe(param.To,
                "cancellation should have stopped before reaching the target value {0}", param.To);
        }
    }

    /// <summary>
    /// Helper: Calculates the expected total number of steps for a given duration.
    /// Mirrors the TransitionRunner logic of Math.Max(1, durationMs / 16).
    /// </summary>
    private static int GetExpectedTotalSteps(int durationMs)
    {
        return Math.Max(1, durationMs / 16);
    }
}
