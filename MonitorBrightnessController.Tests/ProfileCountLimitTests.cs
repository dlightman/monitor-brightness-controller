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
/// Minimal in-memory <see cref="ISettingsStore"/> used by the Profile Count Limit property
/// test. It keeps the current <see cref="AppSettings"/> in a field that <see cref="Load"/>
/// returns and <see cref="Save"/> replaces. Named uniquely to avoid collisions with other
/// test fakes in the suite.
/// </summary>
internal sealed class InMemorySettingsStore_CountLimit : ISettingsStore
{
    private AppSettings _settings;

    public InMemorySettingsStore_CountLimit(AppSettings initial)
    {
        _settings = initial;
    }

    public AppSettings Load() => _settings;

    public Result<Unit> Save(AppSettings settings)
    {
        _settings = settings;
        return Result<Unit>.Success(Unit.Value);
    }
}

/// <summary>
/// FsCheck generator for a new, valid profile name (1-64 chars of <c>[a-zA-Z0-9_-]</c>) used
/// when attempting to exceed the profile count limit.
/// </summary>
public static class ProfileCountLimitArbitraries
{
    private static readonly char[] NameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

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

    private static Gen<string> GenProfileName() =>
        from len in Gen.Choose(1, 64)
        from chars in GenListOfLength(len, Gen.Elements(NameChars))
        select new string(chars.ToArray());

    /// <summary>Arbitrary producing a single valid profile name.</summary>
    public static Arbitrary<string> ValidProfileName() => Arb.From(GenProfileName());
}

/// <summary>
/// Property and example tests for the maximum stored profile count (Property 9).
/// </summary>
public class ProfileCountLimitTests
{
    /// <summary>
    /// Builds an <see cref="AppSettings"/> pre-filled with exactly <see cref="ProfileManager.MaxProfiles"/>
    /// profiles whose names are guaranteed distinct from <paramref name="newName"/> (and from each
    /// other), so that any rejection on creation is attributable to the count limit rather than a
    /// name collision.
    /// </summary>
    private static AppSettings BuildFullSettings(string newName)
    {
        var profiles = new List<Profile>(ProfileManager.MaxProfiles);
        for (int i = 0; i < ProfileManager.MaxProfiles; i++)
        {
            // '#' is not in the valid name charset, so these names can never equal a generated
            // valid newName; the suffix index keeps them distinct from one another.
            profiles.Add(new Profile { Name = $"{newName}#{i}" });
        }

        return new AppSettings { Profiles = profiles };
    }

    // Feature: monitor-brightness-controller, Property 9: Profile Count Limit
    // Validates: Requirements 4.2
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProfileCountLimitArbitraries) })]
    public void ProfileCount_EnforcesLimit(string newName)
    {
        var store = new InMemorySettingsStore_CountLimit(BuildFullSettings(newName));
        var manager = new ProfileManager(store);

        manager.GetAllProfiles().Should().HaveCount(ProfileManager.MaxProfiles);

        Result<Unit> result = manager.CreateProfile(
            newName,
            new Dictionary<string, int> { ["\\\\?\\DISPLAY#NEW#1&xyz"] = 50 });

        // Creation must be rejected and the stored profile count must remain at the maximum.
        result.IsSuccess.Should().BeFalse();
        manager.GetAllProfiles().Should().HaveCount(ProfileManager.MaxProfiles);
    }

    [Fact]
    public void ProfileCount_EnforcesLimit_ConcreteExample()
    {
        var store = new InMemorySettingsStore_CountLimit(BuildFullSettings("focus"));
        var manager = new ProfileManager(store);

        Result<Unit> result = manager.CreateProfile(
            "focus",
            new Dictionary<string, int> { ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 40 });

        result.IsSuccess.Should().BeFalse();
        manager.GetAllProfiles().Should().HaveCount(50);
    }
}
