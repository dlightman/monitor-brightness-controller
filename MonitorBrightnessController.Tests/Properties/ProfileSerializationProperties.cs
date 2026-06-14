using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: gamma-control, Property 8: Profile serialization round-trip preserves both mappings
// Feature: gamma-control, Property 9: Legacy profile deserializes with null gamma map
// Feature: gamma-control, Property 10: Null gamma map omitted from serialized JSON
// Feature: gamma-control, Property 11: Out-of-range gamma values in JSON yield null gamma map on load

/// <summary>
/// Custom arbitraries for profile serialization property tests.
/// </summary>
public static class ProfileSerializationArbitraries
{
    /// <summary>
    /// Generates a valid profile name (1-64 chars, [a-zA-Z0-9_-]).
    /// </summary>
    public static Arbitrary<string> ValidProfileName()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();
        var gen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));
        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a valid device path string (non-empty, alphanumeric with path-like characters).
    /// </summary>
    public static Arbitrary<string> DevicePath()
    {
        var gen = Gen.Choose(1, 5)
            .SelectMany(count =>
                Gen.ArrayOf(count, Gen.Elements("DISPLAY", "DEL", "GSM", "ACI", "SAM", "LGD"))
                    .Select(parts => $"\\\\?\\DISPLAY#{string.Join("#", parts)}#{Guid.NewGuid():N}"));
        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a valid brightness/gamma value in [0, 100].
    /// </summary>
    public static Arbitrary<int> ValidValue()
    {
        return Arb.From(Gen.Choose(0, 100));
    }

    /// <summary>
    /// Generates an out-of-range gamma value (below 0 or above 100).
    /// </summary>
    public static Arbitrary<int> OutOfRangeValue()
    {
        var gen = Gen.OneOf(
            Gen.Choose(-1000, -1),
            Gen.Choose(101, 1000));
        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for profile serialization with gamma support.
/// Tests round-trip preservation, legacy deserialization, null gamma omission,
/// and out-of-range gamma sanitization.
/// </summary>
public class ProfileSerializationProperties
{
    /// <summary>
    /// Property 8: For any valid Profile containing both a MonitorBrightnessMap and a MonitorGammaMap
    /// with all values in [0, 100], serializing to JSON and deserializing back produces an equivalent
    /// Profile with both mappings intact.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.1, 8.3**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property ProfileRoundTrip_PreservesBothMappings()
    {
        var monitorCountGen = Gen.Choose(1, 5);
        var valueGen = Gen.Choose(0, 100);
        var nameGen = Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
            .ArrayOf(Gen.Choose(3, 10).Sample(0, 1).Single())
            .Select(c => new string(c));

        var profileGen =
            from count in monitorCountGen
            from name in Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
                .ArrayOf(8)
                .Select(c => new string(c))
            from brightnessValues in Gen.ArrayOf(count, valueGen)
            from gammaValues in Gen.ArrayOf(count, valueGen)
            let paths = Enumerable.Range(0, count)
                .Select(i => $"\\\\?\\DISPLAY#MON{i}#path{i}")
                .ToArray()
            select new Profile
            {
                Name = name,
                MonitorBrightnessMap = paths.Zip(brightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = paths.Zip(gammaValues)
                    .ToDictionary(p => p.First, p => p.Second)
            };

        return Prop.ForAll(Arb.From(profileGen), profile =>
        {
            // Serialize
            string json = JsonSerializer.Serialize(profile, SettingsStore.SerializerOptions);

            // Deserialize
            var deserialized = JsonSerializer.Deserialize<Profile>(json, SettingsStore.SerializerOptions);

            // Verify equality
            deserialized.Should().NotBeNull();
            deserialized!.Name.Should().Be(profile.Name);
            deserialized.MonitorBrightnessMap.Should().BeEquivalentTo(profile.MonitorBrightnessMap);
            deserialized.MonitorGammaMap.Should().NotBeNull();
            deserialized.MonitorGammaMap.Should().BeEquivalentTo(profile.MonitorGammaMap);
        });
    }

    /// <summary>
    /// Property 9: For any valid profile JSON that contains a brightness mapping but no gamma mapping
    /// property, deserialization produces a Profile where MonitorGammaMap is null.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 8.1**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property LegacyProfile_DeserializesWithNullGammaMap()
    {
        var monitorCountGen = Gen.Choose(1, 5);
        var valueGen = Gen.Choose(0, 100);

        var gen =
            from count in monitorCountGen
            from name in Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
                .ArrayOf(8)
                .Select(c => new string(c))
            from brightnessValues in Gen.ArrayOf(count, valueGen)
            let paths = Enumerable.Range(0, count)
                .Select(i => $"\\\\?\\DISPLAY#MON{i}#path{i}")
                .ToArray()
            let brightnessMap = paths.Zip(brightnessValues)
                .ToDictionary(p => p.First, p => p.Second)
            select new { Name = name, BrightnessMap = brightnessMap };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            // Build JSON manually without gamma mapping property
            var jsonObj = new Dictionary<string, object>
            {
                ["name"] = data.Name,
                ["monitorBrightnessMap"] = data.BrightnessMap
            };

            string json = JsonSerializer.Serialize(jsonObj, SettingsStore.SerializerOptions);

            // Deserialize as Profile
            var profile = JsonSerializer.Deserialize<Profile>(json, SettingsStore.SerializerOptions);

            // Verify gamma map is null (legacy profile)
            profile.Should().NotBeNull();
            profile!.Name.Should().Be(data.Name);
            profile.MonitorBrightnessMap.Should().BeEquivalentTo(data.BrightnessMap);
            profile.MonitorGammaMap.Should().BeNull(
                "a legacy profile JSON without gamma mapping should deserialize with null MonitorGammaMap");
        });
    }

    /// <summary>
    /// Property 10: For any Profile where MonitorGammaMap is null, serialization produces a JSON
    /// object that does not contain a gamma mapping property key.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 8.4**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property NullGammaMap_OmittedFromSerializedJson()
    {
        var monitorCountGen = Gen.Choose(1, 5);
        var valueGen = Gen.Choose(0, 100);

        var profileGen =
            from count in monitorCountGen
            from name in Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
                .ArrayOf(8)
                .Select(c => new string(c))
            from brightnessValues in Gen.ArrayOf(count, valueGen)
            let paths = Enumerable.Range(0, count)
                .Select(i => $"\\\\?\\DISPLAY#MON{i}#path{i}")
                .ToArray()
            select new Profile
            {
                Name = name,
                MonitorBrightnessMap = paths.Zip(brightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = null
            };

        return Prop.ForAll(Arb.From(profileGen), profile =>
        {
            // Serialize
            string json = JsonSerializer.Serialize(profile, SettingsStore.SerializerOptions);

            // Verify JSON does not contain gamma mapping key
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.TryGetProperty("monitorGammaMap", out _).Should().BeFalse(
                "when MonitorGammaMap is null, the property should be omitted from JSON due to JsonIgnoreCondition.WhenWritingNull");
        });
    }

    /// <summary>
    /// Property 11: For any profile JSON where the gamma mapping property is present but contains
    /// at least one value outside [0, 100], deserialization through SettingsStore produces a Profile
    /// where MonitorGammaMap is null and the brightness mapping is preserved.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 8.5**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property OutOfRangeGammaInJson_YieldsNullGammaMapOnLoad()
    {
        var monitorCountGen = Gen.Choose(1, 5);
        var validValueGen = Gen.Choose(0, 100);
        var invalidValueGen = Gen.OneOf(
            Gen.Choose(-1000, -1),
            Gen.Choose(101, 1000));

        var gen =
            from count in monitorCountGen
            from name in Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
                .ArrayOf(8)
                .Select(c => new string(c))
            from brightnessValues in Gen.ArrayOf(count, validValueGen)
            from gammaValues in Gen.ArrayOf(count, validValueGen)
            from invalidIndex in Gen.Choose(0, count - 1)
            from invalidValue in invalidValueGen
            let paths = Enumerable.Range(0, count)
                .Select(i => $"\\\\?\\DISPLAY#MON{i}#path{i}")
                .ToArray()
            let brightnessMap = paths.Zip(brightnessValues)
                .ToDictionary(p => p.First, p => p.Second)
            let gammaMap = paths.Zip(gammaValues)
                .ToDictionary(p => p.First, p => p.Second)
            select new
            {
                Name = name,
                BrightnessMap = brightnessMap,
                GammaMap = gammaMap,
                InvalidPath = paths[invalidIndex],
                InvalidValue = invalidValue
            };

        return Prop.ForAll(Arb.From(gen), data =>
        {
            // Inject the invalid value into the gamma map
            var gammaMapWithInvalid = new Dictionary<string, int>(data.GammaMap)
            {
                [data.InvalidPath] = data.InvalidValue
            };

            // Build settings JSON with the invalid gamma value
            var settings = new AppSettings
            {
                Profiles = new List<Profile>
                {
                    new()
                    {
                        Name = data.Name,
                        MonitorBrightnessMap = new Dictionary<string, int>(data.BrightnessMap),
                        MonitorGammaMap = gammaMapWithInvalid
                    }
                }
            };

            string json = JsonSerializer.Serialize(settings, SettingsStore.SerializerOptions);

            // Use a temp file to go through SettingsStore's Load (which sanitizes)
            string tempFile = Path.Combine(Path.GetTempPath(), $"pbt_gamma_{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(tempFile, json);
                var store = new SettingsStore(tempFile);
                var loaded = store.Load();

                // Verify gamma map is null (sanitized) but brightness is preserved
                loaded.Profiles.Should().HaveCount(1);
                loaded.Profiles[0].MonitorGammaMap.Should().BeNull(
                    "out-of-range gamma values should cause the entire gamma map to be treated as null");
                loaded.Profiles[0].MonitorBrightnessMap.Should().BeEquivalentTo(data.BrightnessMap,
                    "brightness mapping should be preserved even when gamma is invalid");
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        });
    }
}
