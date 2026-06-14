using System;
using System.Collections.Generic;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 2: Out-of-range gamma values are rejected
// Feature: gamma-control, Property 3: Successful gamma set updates monitor state
// Feature: gamma-control, Property 4: Non-existent monitor index returns failure

/// <summary>
/// Custom arbitraries for MonitorService gamma validation property tests.
/// </summary>
public static class GammaValidationArbitraries
{
    /// <summary>
    /// Generates integer values that are outside the valid gamma range [0, 100].
    /// Produces values less than 0 or greater than 100.
    /// </summary>
    public static Arbitrary<int> OutOfRangeGamma()
    {
        var gen = Gen.OneOf(
            Gen.Choose(int.MinValue, -1),
            Gen.Choose(101, int.MaxValue));

        return Arb.From(gen);
    }

    /// <summary>
    /// Generates integer values within the valid gamma range [0, 100].
    /// </summary>
    public static Arbitrary<int> ValidGamma()
    {
        return Arb.From(Gen.Choose(0, 100));
    }

    /// <summary>
    /// Generates positive integer indices that do NOT exist in a single-monitor setup
    /// (monitor index 1 is the only valid one). Produces values != 1.
    /// </summary>
    public static Arbitrary<int> NonExistentMonitorIndex()
    {
        var gen = Gen.OneOf(
            Gen.Choose(int.MinValue, 0),
            Gen.Choose(2, int.MaxValue));

        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for MonitorService gamma validation logic.
/// Tests validation of gamma values, state updates on success, and non-existent monitor handling.
/// </summary>
public class GammaValidationProperties
{
    private static readonly IntPtr TestHandle = new IntPtr(42);

    /// <summary>
    /// Creates a MonitorService with a single controllable monitor at index 1,
    /// using NSubstitute to mock the IMonitorInterop.
    /// </summary>
    private static (MonitorService Service, IMonitorInterop Interop) CreateServiceWithOneMonitor()
    {
        var interop = Substitute.For<IMonitorInterop>();

        interop.EnumerateMonitors().Returns(new List<PhysicalMonitorInfo>
        {
            new PhysicalMonitorInfo
            {
                DevicePath = "\\\\?\\DISPLAY#TEST#1",
                MonitorName = "Test Monitor",
                PhysicalHandle = TestHandle,
                SupportsDdcCi = true
            }
        });

        // Default brightness read succeeds so DetectMonitors completes
        interop.GetBrightness(TestHandle).Returns(Result<int>.Success(50));

        var service = new MonitorService(interop);
        service.DetectMonitors();

        return (service, interop);
    }

    /// <summary>
    /// Property 2: For any integer gamma value outside [0, 100],
    /// SetGamma returns a failure result without invoking any DDC/CI interop method.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.2**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GammaValidationArbitraries) })]
    public void OutOfRangeGamma_IsRejectedWithoutDdcCiCall(int outOfRangeValue)
    {
        // Constrain to out-of-range values
        if (outOfRangeValue >= 0 && outOfRangeValue <= 100)
            return;

        var (service, interop) = CreateServiceWithOneMonitor();

        Result<Unit> result = service.SetGamma(1, outOfRangeValue);

        result.IsSuccess.Should().BeFalse(
            "gamma value {0} is outside [0, 100] and should be rejected", outOfRangeValue);
        result.Error.Should().NotBeNullOrWhiteSpace(
            "a rejected gamma value should produce a descriptive error message");

        // Verify no DDC/CI call was made
        interop.DidNotReceive().SetGamma(Arg.Any<IntPtr>(), Arg.Any<int>());
    }

    /// <summary>
    /// Property 3: For any valid gamma value in [0, 100] with mock DDC/CI success,
    /// SetGamma returns success and the in-memory MonitorState reflects the new gamma value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.3**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GammaValidationArbitraries) })]
    public void ValidGamma_WithMockSuccess_UpdatesMonitorState(int validGammaValue)
    {
        // Constrain to valid range
        if (validGammaValue < 0 || validGammaValue > 100)
            return;

        var (service, interop) = CreateServiceWithOneMonitor();

        // Mock SetGamma to return success for this value
        interop.SetGamma(TestHandle, validGammaValue).Returns(Result<Unit>.Success(Unit.Value));

        Result<Unit> result = service.SetGamma(1, validGammaValue);

        result.IsSuccess.Should().BeTrue(
            "gamma value {0} is within [0, 100] and the DDC/CI mock returns success", validGammaValue);

        // Verify state was updated by using FindMonitor to read current state
        MonitorState? monitor = service.FindMonitor("1");
        monitor.Should().NotBeNull();
        monitor!.CurrentGamma.Should().Be(validGammaValue,
            "after a successful SetGamma, the in-memory state should reflect the new value");
        monitor.ErrorMessage.Should().BeNull(
            "after a successful SetGamma, the error message should be cleared");
    }

    /// <summary>
    /// Property 4: For any monitor index that does not match a detected monitor,
    /// SetGamma returns a failure result without invoking any DDC/CI interop method.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.5**
    /// </remarks>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(GammaValidationArbitraries) })]
    public void NonExistentMonitorIndex_ReturnsFailureWithoutDdcCiCall(int invalidIndex)
    {
        // Constrain to indices not in detected list (only index 1 exists)
        if (invalidIndex == 1)
            return;

        var (service, interop) = CreateServiceWithOneMonitor();

        Result<Unit> result = service.SetGamma(invalidIndex, 50);

        result.IsSuccess.Should().BeFalse(
            "monitor index {0} does not exist and should produce a failure", invalidIndex);
        result.Error.Should().NotBeNullOrWhiteSpace(
            "a non-existent monitor index should produce a descriptive error message");

        // Verify no DDC/CI call was made
        interop.DidNotReceive().SetGamma(Arg.Any<IntPtr>(), Arg.Any<int>());
    }
}
