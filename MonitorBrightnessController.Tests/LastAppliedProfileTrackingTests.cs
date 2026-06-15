using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Application;
using MonitorBrightnessController.Interfaces;
using MonitorBrightnessController.Models;
using NSubstitute;
using Xunit;
using MbcUnit = MonitorBrightnessController.Models.Unit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Minimal in-memory <see cref="ISettingsStore"/> used solely by the
/// Last-Applied-Profile-Tracking property test (Property 14). It holds the current
/// <see cref="AppSettings"/> in memory: <see cref="Load"/> returns the held value and
/// <see cref="Save"/> replaces it and reports success, so the test can observe exactly
/// what <see cref="ProfileManager.ApplyProfile"/> persisted.
/// </summary>
public sealed class InMemorySettingsStore_LastApplied : ISettingsStore
{
    /// <summary>The most recently persisted (or seeded) settings.</summary>
    public AppSettings Current { get; private set; }

    public InMemorySettingsStore_LastApplied(AppSettings seed)
    {
        Current = seed;
    }

    public AppSettings Load() => Current;

    public Result<MbcUnit> Save(AppSettings settings)
    {
        Current = settings;
        return Result<MbcUnit>.Success(MbcUnit.Value);
    }
}

/// <summary>
/// Custom FsCheck generators for the Last-Applied-Profile-Tracking property test.
/// Generates valid profile names matching the documented charset/length
/// (1-64 characters drawn from <c>[a-zA-Z0-9_-]</c>).
/// </summary>
public static class LastAppliedProfileArbitraries
{
    private static readonly char[] NameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    /// <summary>
    /// Generates a list of exactly <paramref name="n"/> elements using only core Gen
    /// combinators (version-agnostic across FsCheck releases).
    /// </summary>
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

    private static Gen<string> GenValidProfileName() =>
        from len in Gen.Choose(1, 64)
        from chars in GenListOfLength(len, Gen.Elements(NameChars))
        select new string(chars.ToArray());

    /// <summary>Arbitrary producing valid profile names accepted by the validation rules.</summary>
    public static Arbitrary<string> ValidProfileName() => Arb.From(GenValidProfileName());
}

/// <summary>
/// Property and example tests for last-applied-profile tracking on successful profile
/// application (Property 14).
/// </summary>
public class LastAppliedProfileTrackingTests
{
    private const string DevicePath = @"\\?\DISPLAY#DEL41AB#5&lastapplied";

    // Feature: monitor-brightness-controller, Property 14: Last Applied Profile Tracking
    // Validates: Requirements 5.5
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LastAppliedProfileArbitraries) })]
    public void LastProfile_TrackedOnApply(string profileName)
    {
        // Seed the store with a single profile (named with the generated valid name) that
        // maps one device path to a brightness value. Start with no last-applied profile.
        var seed = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = profileName,
                    MonitorBrightnessMap = new Dictionary<string, int> { [DevicePath] = 42 },
                },
            },
            LastAppliedProfileName = null,
        };
        var store = new InMemorySettingsStore_LastApplied(seed);

        // A monitor service that reports one connected monitor matching the profile's device
        // path, and successfully applies any brightness value.
        var monitorService = Substitute.For<IMonitorService>();
        monitorService.DetectMonitors().Returns(new List<MonitorState>
        {
            new()
            {
                MonitorIndex = 1,
                MonitorName = "Monitor 1",
                DevicePath = DevicePath,
                IsControllable = true,
            },
        });
        monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var manager = new ProfileManager(store);

        Result<MbcUnit> result = manager.ApplyProfile(profileName, monitorService);

        result.IsSuccess.Should().BeTrue(
            "applying a profile with one connected mapped monitor should succeed");
        store.Current.LastAppliedProfileName.Should().Be(
            profileName,
            "the successfully applied profile name must be recorded as last-applied");
    }

    [Fact]
    public void LastProfile_TrackedOnApply_ConcreteExample()
    {
        var seed = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "focus",
                    MonitorBrightnessMap = new Dictionary<string, int> { [DevicePath] = 30 },
                },
            },
            LastAppliedProfileName = null,
        };
        var store = new InMemorySettingsStore_LastApplied(seed);

        var monitorService = Substitute.For<IMonitorService>();
        monitorService.DetectMonitors().Returns(new List<MonitorState>
        {
            new()
            {
                MonitorIndex = 1,
                MonitorName = "DELL U2723QE",
                DevicePath = DevicePath,
                IsControllable = true,
            },
        });
        monitorService.SetBrightness(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Result<MbcUnit>.Success(MbcUnit.Value));

        var manager = new ProfileManager(store);

        // Apply using a case-variant name to confirm the stored profile's canonical name is recorded.
        Result<MbcUnit> result = manager.ApplyProfile("FOCUS", monitorService);

        result.IsSuccess.Should().BeTrue();
        store.Current.LastAppliedProfileName.Should().Be("focus");
    }
}
