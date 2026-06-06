using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Custom FsCheck generators/arbitraries for <see cref="AppSettings"/> and its constituent
/// types. Profile names match the documented charset/length (1-64 of [a-zA-Z0-9_-]);
/// brightness map keys are non-empty device-path-like strings; brightness values are in [0,100].
/// </summary>
public static class AppSettingsArbitraries
{
    private static readonly char[] NameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    // Device-path-like characters: letters, digits and the punctuation that appears in
    // Windows device interface paths (e.g. \\?\DISPLAY#DEL41AB#5&...).
    private static readonly char[] PathChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789\\#&?.{}-_".ToCharArray();

    /// <summary>
    /// Generates a list of exactly <paramref name="n"/> elements from <paramref name="elementGen"/>,
    /// using only the core Gen combinators (so it works across FsCheck versions).
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

    private static Gen<string> GenProfileName() =>
        from len in Gen.Choose(1, 64)
        from chars in GenListOfLength(len, Gen.Elements(NameChars))
        select new string(chars.ToArray());

    private static Gen<string> GenDevicePath() =>
        from len in Gen.Choose(1, 40)
        from chars in GenListOfLength(len, Gen.Elements(PathChars))
        select new string(chars.ToArray());

    private static Gen<int> GenBrightness() => Gen.Choose(0, 100);

    private static Gen<IReadOnlyDictionary<string, int>> GenBrightnessMap() =>
        from count in Gen.Choose(0, 8)
        from keys in GenListOfLength(count, GenDevicePath())
        from values in GenListOfLength(count, GenBrightness())
        // Zip keys with values; de-duplicate keys (last write wins) so the map is well-formed.
        select (IReadOnlyDictionary<string, int>)keys
            .Zip(values, (k, v) => (Key: k, Value: v))
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => g.Last().Value);

    private static Gen<Profile> GenProfile() =>
        from name in GenProfileName()
        from map in GenBrightnessMap()
        select new Profile { Name = name, MonitorBrightnessMap = map };

    private static Gen<string?> GenOptionalLastApplied() =>
        Gen.OneOf(
            Gen.Constant((string?)null),
            GenProfileName().Select(n => (string?)n));

    private static Gen<AppSettings> GenAppSettings() =>
        from count in Gen.Choose(0, 10)
        from profiles in GenListOfLength(count, GenProfile())
        from autoApply in Arb.Generate<bool>()
        from lastApplied in GenOptionalLastApplied()
        select new AppSettings
        {
            Profiles = profiles,
            AutoApplyOnStartup = autoApply,
            LastAppliedProfileName = lastApplied,
        };

    /// <summary>Arbitrary used by the property test to generate random valid settings.</summary>
    public static Arbitrary<AppSettings> AppSettings() => Arb.From(GenAppSettings());
}

/// <summary>
/// Property and example tests for JSON persistence of <see cref="AppSettings"/>.
/// </summary>
public class SettingsSerializationTests
{
    // Feature: monitor-brightness-controller, Property 13: Settings Serialization Round-Trip
    // Validates: Requirements 5.1
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AppSettingsArbitraries) })]
    public void Settings_RoundTrips(AppSettings original)
    {
        var json = JsonSerializer.Serialize(original, SettingsStore.SerializerOptions);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

        // List<Profile> and IReadOnlyDictionary<string,int> do not implement structural
        // equality, so record value equality would compare them by reference. Use a deep
        // structural comparison instead.
        restored.Should().NotBeNull();
        restored!.AutoApplyOnStartup.Should().Be(original.AutoApplyOnStartup);
        restored.LastAppliedProfileName.Should().Be(original.LastAppliedProfileName);
        restored.Profiles.Should().BeEquivalentTo(
            original.Profiles,
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Settings_RoundTrips_ConcreteExample()
    {
        var original = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "focus",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 40,
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 60,
                    },
                },
                new()
                {
                    Name = "movie_mode-1",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 0,
                    },
                },
            },
            AutoApplyOnStartup = true,
            LastAppliedProfileName = "focus",
        };

        var json = JsonSerializer.Serialize(original, SettingsStore.SerializerOptions);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

        restored.Should().NotBeNull();
        restored!.AutoApplyOnStartup.Should().BeTrue();
        restored.LastAppliedProfileName.Should().Be("focus");
        restored.Profiles.Should().BeEquivalentTo(original.Profiles, o => o.WithStrictOrdering());
    }
}
