using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// A single monitor entry used to build a profile: its device path, the brightness value
/// the profile maps it to, and whether the monitor is currently connected.
/// </summary>
public sealed record ApplySkipMonitorEntry(string DevicePath, int Brightness, bool IsConnected);

/// <summary>
/// A generated scenario for Property 11: a profile mapping N monitors (with distinct device
/// paths) to brightness values, where a random subset is currently connected.
/// </summary>
public sealed record ApplySkipScenario(IReadOnlyList<ApplySkipMonitorEntry> Entries);

/// <summary>
/// Minimal in-memory <see cref="ISettingsStore"/> used by the Property 11 test. Holds a
/// single <see cref="AppSettings"/> instance in memory and records the last saved value so
/// that LastAppliedProfileName updates do not require touching disk. Uniquely named to avoid
/// clashing with fakes in other test files.
/// </summary>
internal sealed class InMemorySettingsStore_ApplySkip : ISettingsStore
{
    private AppSettings _settings;

    public InMemorySettingsStore_ApplySkip(AppSettings initial) => _settings = initial;

    public AppSettings Load() => _settings;

    public Result<Unit> Save(AppSettings settings)
    {
        _settings = settings;
        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>
/// Fake <see cref="IMonitorService"/> for the Property 11 test. <see cref="DetectMonitors"/>
/// returns only the connected monitors (each with an assigned index), and
/// <see cref="SetBrightness"/> records every (monitorIndex, value) call so the test can verify
/// which monitors had brightness applied. Uniquely named to avoid clashing with other fakes.
/// </summary>
internal sealed class RecordingMonitorService_ApplySkip : IMonitorService
{
    private readonly IReadOnlyList<MonitorState> _connected;

    /// <summary>Records each successful SetBrightness call as (monitorIndex, value), in order.</summary>
    public List<(int MonitorIndex, int Value)> SetBrightnessCalls { get; } = new();

    public RecordingMonitorService_ApplySkip(IReadOnlyList<MonitorState> connected) => _connected = connected;

    public IReadOnlyList<MonitorState> DetectMonitors() => _connected;

    public Result<Unit> SetBrightness(int monitorIndex, int brightnessValue)
    {
        SetBrightnessCalls.Add((monitorIndex, brightnessValue));
        return Result<Unit>.Success(Unit.Value);
    }

    public Result<int> GetBrightness(int monitorIndex) =>
        throw new NotSupportedException("Not exercised by Property 11.");

    public MonitorState? FindMonitor(string identifier) =>
        throw new NotSupportedException("Not exercised by Property 11.");
}

/// <summary>
/// Custom FsCheck generators for Property 11. Produces a profile mapping N (1-8) monitors with
/// guaranteed-distinct device paths to brightness values in [0, 100], together with a random
/// connected/disconnected flag per monitor.
/// </summary>
public static class ApplySkipArbitraries
{
    private static readonly char[] PathChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

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

    private static Gen<string> GenPathSuffix() =>
        from len in Gen.Choose(1, 12)
        from chars in GenListOfLength(len, Gen.Elements(PathChars))
        select new string(chars.ToArray());

    private static Gen<ApplySkipScenario> GenScenario() =>
        from count in Gen.Choose(1, 8)
        from suffixes in GenListOfLength(count, GenPathSuffix())
        from brightnesses in GenListOfLength(count, Gen.Choose(0, 100))
        from connectedFlags in GenListOfLength(count, Arb.Generate<bool>())
        let entries = Enumerable.Range(0, count)
            .Select(i => new ApplySkipMonitorEntry(
                // Append the loop index so device paths are guaranteed distinct within a case.
                DevicePath: $@"\\?\DISPLAY#{suffixes[i]}#{i}",
                Brightness: brightnesses[i],
                IsConnected: connectedFlags[i]))
            .ToList()
        select new ApplySkipScenario(entries);

    /// <summary>Arbitrary used by the property test.</summary>
    public static Arbitrary<ApplySkipScenario> Scenario() => Arb.From(GenScenario());
}

/// <summary>
/// Property and example tests verifying that applying a profile sets brightness on connected
/// monitors, skips disconnected ones, and succeeds iff at least one mapped monitor is connected.
/// </summary>
public class ProfileApplySkipsDisconnectedTests
{
    // Feature: monitor-brightness-controller, Property 11: Profile Application Skips Disconnected Monitors
    // Validates: Requirements 4.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ApplySkipArbitraries) })]
    public void ProfileApply_SkipsDisconnected(ApplySkipScenario scenario)
    {
        const string profileName = "test-profile";

        // Build the profile mapping every monitor (connected and disconnected) by device path.
        var map = scenario.Entries.ToDictionary(e => e.DevicePath, e => e.Brightness);
        var settings = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new() { Name = profileName, MonitorBrightnessMap = map },
            },
        };
        var store = new InMemorySettingsStore_ApplySkip(settings);

        // Only the connected subset is "detected"; assign each a stable monitor index.
        var connectedEntries = scenario.Entries.Where(e => e.IsConnected).ToList();
        var connectedStates = connectedEntries
            .Select((e, i) => new MonitorState
            {
                MonitorIndex = i + 1,
                MonitorName = $"Monitor {i + 1}",
                DevicePath = e.DevicePath,
                IsControllable = true,
            })
            .ToList();
        var monitorService = new RecordingMonitorService_ApplySkip(connectedStates);

        var manager = new ProfileManager(store);
        Result<Unit> result = manager.ApplyProfile(profileName, monitorService);

        // Expected: brightness set exactly once per connected monitor, with its mapped value.
        var expectedCalls = connectedStates
            .Select(s => (s.MonitorIndex, Value: map[s.DevicePath]))
            .ToList();

        // All connected monitors had brightness applied (with the correct values); none of the
        // disconnected monitors did. Order-independent comparison of the recorded calls.
        monitorService.SetBrightnessCalls.Should().BeEquivalentTo(expectedCalls);

        // Success iff the connected subset C is non-empty.
        bool anyConnected = connectedEntries.Count > 0;
        result.IsSuccess.Should().Be(anyConnected);
    }

    [Fact]
    public void ProfileApply_SkipsDisconnected_ConcreteExample()
    {
        const string profileName = "focus";
        var map = new Dictionary<string, int>
        {
            [@"\\?\DISPLAY#CONNECTED-A#0"] = 40,
            [@"\\?\DISPLAY#DISCONNECTED-B#1"] = 70,
            [@"\\?\DISPLAY#CONNECTED-C#2"] = 90,
        };
        var settings = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new() { Name = profileName, MonitorBrightnessMap = map },
            },
        };
        var store = new InMemorySettingsStore_ApplySkip(settings);

        var connectedStates = new List<MonitorState>
        {
            new() { MonitorIndex = 1, MonitorName = "A", DevicePath = @"\\?\DISPLAY#CONNECTED-A#0", IsControllable = true },
            new() { MonitorIndex = 2, MonitorName = "C", DevicePath = @"\\?\DISPLAY#CONNECTED-C#2", IsControllable = true },
        };
        var monitorService = new RecordingMonitorService_ApplySkip(connectedStates);

        var manager = new ProfileManager(store);
        Result<Unit> result = manager.ApplyProfile(profileName, monitorService);

        result.IsSuccess.Should().BeTrue();
        monitorService.SetBrightnessCalls.Should().BeEquivalentTo(new[]
        {
            (MonitorIndex: 1, Value: 40),
            (MonitorIndex: 2, Value: 90),
        });
    }

    [Fact]
    public void ProfileApply_AllDisconnected_Fails()
    {
        const string profileName = "movie";
        var map = new Dictionary<string, int>
        {
            [@"\\?\DISPLAY#GONE-A#0"] = 30,
            [@"\\?\DISPLAY#GONE-B#1"] = 50,
        };
        var settings = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new() { Name = profileName, MonitorBrightnessMap = map },
            },
        };
        var store = new InMemorySettingsStore_ApplySkip(settings);

        // No monitors connected.
        var monitorService = new RecordingMonitorService_ApplySkip(new List<MonitorState>());

        var manager = new ProfileManager(store);
        Result<Unit> result = manager.ApplyProfile(profileName, monitorService);

        result.IsSuccess.Should().BeFalse();
        monitorService.SetBrightnessCalls.Should().BeEmpty();
    }
}
