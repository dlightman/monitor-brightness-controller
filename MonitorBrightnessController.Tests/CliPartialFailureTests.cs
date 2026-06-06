using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// A single monitor in a Property 8 scenario: identified by a 1-based index and a friendly
/// name, with a flag indicating whether its <c>SetBrightness</c> call should succeed or fail.
/// </summary>
public sealed record PartialFailureMonitor(int Index, string Name, bool ShouldSucceed);

/// <summary>
/// A generated scenario for Property 8: N (1-8) monitors, each with a distinct index/name and a
/// success/failure flag, used to verify that partial failures still attempt every monitor.
/// </summary>
public sealed record PartialFailureScenario(IReadOnlyList<PartialFailureMonitor> Monitors);

/// <summary>
/// Fake <see cref="IMonitorService"/> for the Property 8 (partial failure) test. It detects a
/// fixed set of monitors, resolves identifiers by index or case-insensitive name, and returns a
/// per-monitor success/failure result from <see cref="SetBrightness"/> while recording every
/// call so the test can assert that all operations were attempted. Uniquely named to avoid
/// clashing with fakes in other test files.
/// </summary>
internal sealed class RecordingMonitorService_PartialFailure : IMonitorService
{
    private readonly IReadOnlyList<MonitorState> _monitors;
    private readonly IReadOnlyDictionary<int, bool> _successByIndex;

    /// <summary>Records each SetBrightness call as (monitorIndex, value), in invocation order.</summary>
    public List<(int MonitorIndex, int Value)> SetBrightnessCalls { get; } = new();

    public RecordingMonitorService_PartialFailure(
        IReadOnlyList<MonitorState> monitors,
        IReadOnlyDictionary<int, bool> successByIndex)
    {
        _monitors = monitors;
        _successByIndex = successByIndex;
    }

    public IReadOnlyList<MonitorState> DetectMonitors() => _monitors;

    public Result<Unit> SetBrightness(int monitorIndex, int brightnessValue)
    {
        // Record every attempt regardless of outcome (this is the core of Property 8).
        SetBrightnessCalls.Add((monitorIndex, brightnessValue));

        return _successByIndex.TryGetValue(monitorIndex, out bool ok) && ok
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure($"DDC/CI communication failed for monitor {monitorIndex}");
    }

    public Result<int> GetBrightness(int monitorIndex) =>
        throw new NotSupportedException("Not exercised by Property 8.");

    public MonitorState? FindMonitor(string identifier)
    {
        if (int.TryParse(identifier, out int index))
        {
            return _monitors.FirstOrDefault(m => m.MonitorIndex == index);
        }

        return _monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorName, identifier, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Custom FsCheck generators for Property 8. Produces N (1-8) monitors with distinct indices
/// (1..N), distinct names ("mon1".."monN"), and an independent success/failure flag each.
/// </summary>
public static class PartialFailureArbitraries
{
    private static Gen<List<T>> GenListOfLength<T>(int n, Gen<T> elementGen)
    {
        if (n <= 0)
        {
            return Gen.Constant(new List<T>());
        }

        return elementGen.SelectMany(head =>
            GenListOfLength(n - 1, elementGen).Select(tail =>
            {
                tail.Insert(0, head);
                return tail;
            }));
    }

    private static Gen<PartialFailureScenario> GenScenario() =>
        from count in Gen.Choose(1, 8)
        from flags in GenListOfLength(count, Arb.Generate<bool>())
        let monitors = Enumerable.Range(0, count)
            .Select(i => new PartialFailureMonitor(
                Index: i + 1,
                Name: $"mon{i + 1}",
                ShouldSucceed: flags[i]))
            .ToList()
        select new PartialFailureScenario(monitors);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<PartialFailureScenario> Scenario() => Arb.From(GenScenario());
}

/// <summary>
/// Property and example tests for Property 8: a partial-failure CLI invocation must attempt
/// every monitor-brightness operation, apply brightness to all monitors that succeed, and
/// report an error for every monitor that fails (exit code 1 iff any fail).
/// </summary>
public class CliPartialFailureTests
{
    private const int Brightness = 50;

    private static IReadOnlyList<MonitorState> BuildMonitorStates(PartialFailureScenario scenario) =>
        scenario.Monitors
            .Select(m => new MonitorState
            {
                MonitorIndex = m.Index,
                MonitorName = m.Name,
                DevicePath = $@"\\?\DISPLAY#{m.Name}#{m.Index}",
                IsControllable = true,
            })
            .ToList();

    private static string[] BuildArgs(PartialFailureScenario scenario) =>
        scenario.Monitors
            .SelectMany(m => new[] { "--monitor", m.Name, "--brightness", Brightness.ToString() })
            .ToArray();

    // Feature: monitor-brightness-controller, Property 8: Partial Failure Attempts All Monitors
    // Validates: Requirements 3.7
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PartialFailureArbitraries) })]
    public void PartialFailure_AttemptsAll(PartialFailureScenario scenario)
    {
        var monitors = BuildMonitorStates(scenario);
        var successByIndex = scenario.Monitors.ToDictionary(m => m.Index, m => m.ShouldSucceed);
        var monitorService = new RecordingMonitorService_PartialFailure(monitors, successByIndex);
        var profileManager = Substitute.For<IProfileManager>();

        using var stdErr = new StringWriter();
        using var stdOut = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, stdOut, stdErr);

        int exitCode = handler.Execute(BuildArgs(scenario));

        // Every monitor must have been attempted exactly once, with the supplied brightness.
        var expectedCalls = scenario.Monitors
            .Select(m => (MonitorIndex: m.Index, Value: Brightness))
            .ToList();
        monitorService.SetBrightnessCalls.Should().BeEquivalentTo(
            expectedCalls,
            options => options.WithStrictOrdering());

        // Exit code is 1 iff at least one monitor failed, else 0.
        bool anyFailed = scenario.Monitors.Any(m => !m.ShouldSucceed);
        exitCode.Should().Be(anyFailed ? 1 : 0);

        // Each failed monitor must have produced an individual error line on stderr.
        string errorOutput = stdErr.ToString();
        foreach (PartialFailureMonitor failed in scenario.Monitors.Where(m => !m.ShouldSucceed))
        {
            errorOutput.Should().Contain($"Failed to set brightness on monitor '{failed.Name}'");
        }
    }

    [Fact]
    public void PartialFailure_MixedSuccessAndFailure_ConcreteExample()
    {
        var scenario = new PartialFailureScenario(new List<PartialFailureMonitor>
        {
            new(1, "mon1", ShouldSucceed: true),
            new(2, "mon2", ShouldSucceed: false),
            new(3, "mon3", ShouldSucceed: true),
        });

        var monitors = BuildMonitorStates(scenario);
        var successByIndex = scenario.Monitors.ToDictionary(m => m.Index, m => m.ShouldSucceed);
        var monitorService = new RecordingMonitorService_PartialFailure(monitors, successByIndex);
        var profileManager = Substitute.For<IProfileManager>();

        using var stdErr = new StringWriter();
        using var stdOut = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, stdOut, stdErr);

        int exitCode = handler.Execute(BuildArgs(scenario));

        // All three monitors attempted even though monitor 2 failed.
        monitorService.SetBrightnessCalls.Should().BeEquivalentTo(new[]
        {
            (MonitorIndex: 1, Value: 50),
            (MonitorIndex: 2, Value: 50),
            (MonitorIndex: 3, Value: 50),
        }, options => options.WithStrictOrdering());

        exitCode.Should().Be(1);
        stdErr.ToString().Should().Contain("Failed to set brightness on monitor 'mon2'");
    }

    [Fact]
    public void PartialFailure_AllSucceed_ReturnsZero()
    {
        var scenario = new PartialFailureScenario(new List<PartialFailureMonitor>
        {
            new(1, "mon1", ShouldSucceed: true),
            new(2, "mon2", ShouldSucceed: true),
        });

        var monitors = BuildMonitorStates(scenario);
        var successByIndex = scenario.Monitors.ToDictionary(m => m.Index, m => m.ShouldSucceed);
        var monitorService = new RecordingMonitorService_PartialFailure(monitors, successByIndex);
        var profileManager = Substitute.For<IProfileManager>();

        using var stdErr = new StringWriter();
        using var stdOut = new StringWriter();
        var handler = new CliHandler(monitorService, profileManager, stdOut, stdErr);

        int exitCode = handler.Execute(BuildArgs(scenario));

        exitCode.Should().Be(0);
        monitorService.SetBrightnessCalls.Should().HaveCount(2);
        stdErr.ToString().Should().BeEmpty();
    }
}
