using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Verifies backward-compatible deserialization of profiles with respect to the gamma mapping.
/// Requirements: 8.1, 8.3, 8.4
/// </summary>
public class ProfileGammaDeserializationTests
{
    /// <summary>
    /// Requirement 8.1: A legacy profile JSON with no gamma mapping property deserializes
    /// with MonitorGammaMap set to null.
    /// </summary>
    [Fact]
    public void LegacyProfile_WithoutGammaProperty_DeserializesAsNullGammaMap()
    {
        // Arrange: JSON that mimics a pre-gamma profile (brightness only, no monitorGammaMap key)
        const string legacyJson = """
            {
                "name": "legacy-profile",
                "monitorBrightnessMap": {
                    "\\\\?\\DISPLAY#DEL41AB#5&abc": 50,
                    "\\\\?\\DISPLAY#GSM59AB#7&def": 75
                }
            }
            """;

        // Act
        var profile = JsonSerializer.Deserialize<Profile>(legacyJson, SettingsStore.SerializerOptions);

        // Assert
        profile.Should().NotBeNull();
        profile!.Name.Should().Be("legacy-profile");
        profile.MonitorBrightnessMap.Should().HaveCount(2);
        profile.MonitorBrightnessMap["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(50);
        profile.MonitorBrightnessMap["\\\\?\\DISPLAY#GSM59AB#7&def"].Should().Be(75);
        profile.MonitorGammaMap.Should().BeNull();
    }

    /// <summary>
    /// Requirement 8.4: A profile with null MonitorGammaMap serializes without the
    /// monitorGammaMap property key in JSON output.
    /// </summary>
    [Fact]
    public void NullGammaMap_IsOmittedFromSerializedJson()
    {
        // Arrange
        var profile = new Profile
        {
            Name = "brightness-only",
            MonitorBrightnessMap = new Dictionary<string, int>
            {
                ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 40
            },
            MonitorGammaMap = null
        };

        // Act
        var json = JsonSerializer.Serialize(profile, SettingsStore.SerializerOptions);

        // Assert: JSON should NOT contain the monitorGammaMap key
        json.Should().NotContain("monitorGammaMap");
        // But should contain the brightness mapping
        json.Should().Contain("monitorBrightnessMap");
        json.Should().Contain("brightness-only");
    }

    /// <summary>
    /// Requirement 8.3: A profile with both brightness and gamma mappings round-trips correctly.
    /// </summary>
    [Fact]
    public void ProfileWithGammaMap_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var original = new Profile
        {
            Name = "full-profile",
            MonitorBrightnessMap = new Dictionary<string, int>
            {
                ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 40,
                ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 80
            },
            MonitorGammaMap = new Dictionary<string, int>
            {
                ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 60,
                ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 90
            }
        };

        // Act
        var json = JsonSerializer.Serialize(original, SettingsStore.SerializerOptions);
        var restored = JsonSerializer.Deserialize<Profile>(json, SettingsStore.SerializerOptions);

        // Assert
        restored.Should().NotBeNull();
        restored!.Name.Should().Be("full-profile");
        restored.MonitorBrightnessMap.Should().BeEquivalentTo(original.MonitorBrightnessMap);
        restored.MonitorGammaMap.Should().NotBeNull();
        restored.MonitorGammaMap.Should().BeEquivalentTo(original.MonitorGammaMap);
        // Verify JSON contains the gamma mapping key
        json.Should().Contain("monitorGammaMap");
    }

    /// <summary>
    /// Requirement 8.1: Full AppSettings round-trip with a legacy profile (no gamma) works
    /// through the SettingsStore serializer options.
    /// </summary>
    [Fact]
    public void AppSettings_WithLegacyProfile_RoundTripsCorrectly()
    {
        // Arrange: simulate loading settings that contain a legacy brightness-only profile
        const string settingsJson = """
            {
                "profiles": [
                    {
                        "name": "old-profile",
                        "monitorBrightnessMap": {
                            "\\\\?\\DISPLAY#DEL41AB#5&abc": 30
                        }
                    }
                ],
                "autoApplyOnStartup": false,
                "lastAppliedProfileName": "old-profile"
            }
            """;

        // Act
        var settings = JsonSerializer.Deserialize<AppSettings>(settingsJson, SettingsStore.SerializerOptions);

        // Assert
        settings.Should().NotBeNull();
        settings!.Profiles.Should().HaveCount(1);
        settings.Profiles[0].Name.Should().Be("old-profile");
        settings.Profiles[0].MonitorBrightnessMap["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(30);
        settings.Profiles[0].MonitorGammaMap.Should().BeNull();
    }

    /// <summary>
    /// Requirement 8.4: Saving AppSettings with a null-gamma profile omits the gamma key.
    /// </summary>
    [Fact]
    public void AppSettings_SaveWithNullGamma_OmitsGammaProperty()
    {
        // Arrange
        var settings = new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "no-gamma",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 50
                    },
                    MonitorGammaMap = null
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(settings, SettingsStore.SerializerOptions);

        // Assert: the output should not contain monitorGammaMap for the null-gamma profile
        json.Should().NotContain("monitorGammaMap");
    }
}
