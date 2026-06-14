using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Verifies that the SettingsStore sanitizes out-of-range gamma values on load.
/// Requirements: 8.5
/// </summary>
public class SettingsStoreGammaTests : IDisposable
{
    private readonly string _tempFile;

    public SettingsStoreGammaTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    /// <summary>
    /// Requirement 8.5: If a profile's gamma map contains a value above 100,
    /// the entire gamma map is set to null while brightness is preserved.
    /// </summary>
    [Fact]
    public void Load_GammaValueAbove100_SetsGammaMapToNull()
    {
        // Arrange: profile with gamma value 150 (out of range)
        var json = JsonSerializer.Serialize(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "bad-gamma",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 60
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 150
                    }
                }
            }
        }, SettingsStore.SerializerOptions);

        File.WriteAllText(_tempFile, json);
        var store = new SettingsStore(_tempFile);

        // Act
        var settings = store.Load();

        // Assert: gamma map nulled, brightness preserved
        settings.Profiles.Should().HaveCount(1);
        settings.Profiles[0].MonitorGammaMap.Should().BeNull();
        settings.Profiles[0].MonitorBrightnessMap["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(60);
    }

    /// <summary>
    /// Requirement 8.5: If a profile's gamma map contains a negative value,
    /// the entire gamma map is set to null while brightness is preserved.
    /// </summary>
    [Fact]
    public void Load_GammaValueBelowZero_SetsGammaMapToNull()
    {
        // Arrange: profile with gamma value -5 (out of range)
        var json = JsonSerializer.Serialize(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "negative-gamma",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 80
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = -5
                    }
                }
            }
        }, SettingsStore.SerializerOptions);

        File.WriteAllText(_tempFile, json);
        var store = new SettingsStore(_tempFile);

        // Act
        var settings = store.Load();

        // Assert: gamma map nulled, brightness preserved
        settings.Profiles.Should().HaveCount(1);
        settings.Profiles[0].MonitorGammaMap.Should().BeNull();
        settings.Profiles[0].MonitorBrightnessMap["\\\\?\\DISPLAY#GSM59AB#7&def"].Should().Be(80);
    }

    /// <summary>
    /// Requirement 8.5: Valid gamma values in [0, 100] are preserved on load.
    /// </summary>
    [Fact]
    public void Load_ValidGammaValues_PreservesGammaMap()
    {
        // Arrange: profile with all valid gamma values
        var json = JsonSerializer.Serialize(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "valid-gamma",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 50
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 75
                    }
                }
            }
        }, SettingsStore.SerializerOptions);

        File.WriteAllText(_tempFile, json);
        var store = new SettingsStore(_tempFile);

        // Act
        var settings = store.Load();

        // Assert: both maps intact
        settings.Profiles.Should().HaveCount(1);
        settings.Profiles[0].MonitorGammaMap.Should().NotBeNull();
        settings.Profiles[0].MonitorGammaMap!["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(75);
        settings.Profiles[0].MonitorBrightnessMap["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(50);
    }

    /// <summary>
    /// Requirement 8.5: Only the profile with invalid gamma values has its gamma map nulled.
    /// Other profiles in the same settings are not affected.
    /// </summary>
    [Fact]
    public void Load_MixedProfiles_OnlyInvalidGammaProfileIsNulled()
    {
        // Arrange: two profiles, one valid and one invalid
        var json = JsonSerializer.Serialize(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "valid-profile",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 40
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 50
                    }
                },
                new()
                {
                    Name = "invalid-profile",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 70
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 200
                    }
                }
            }
        }, SettingsStore.SerializerOptions);

        File.WriteAllText(_tempFile, json);
        var store = new SettingsStore(_tempFile);

        // Act
        var settings = store.Load();

        // Assert: first profile gamma preserved, second profile gamma nulled
        settings.Profiles.Should().HaveCount(2);

        settings.Profiles[0].Name.Should().Be("valid-profile");
        settings.Profiles[0].MonitorGammaMap.Should().NotBeNull();
        settings.Profiles[0].MonitorGammaMap!["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(50);

        settings.Profiles[1].Name.Should().Be("invalid-profile");
        settings.Profiles[1].MonitorGammaMap.Should().BeNull();
        settings.Profiles[1].MonitorBrightnessMap["\\\\?\\DISPLAY#GSM59AB#7&def"].Should().Be(70);
    }

    /// <summary>
    /// Requirement 8.5: Boundary values 0 and 100 are considered valid and preserved.
    /// </summary>
    [Fact]
    public void Load_BoundaryGammaValues_ArePreserved()
    {
        // Arrange: profile with boundary values 0 and 100
        var json = JsonSerializer.Serialize(new AppSettings
        {
            Profiles = new List<Profile>
            {
                new()
                {
                    Name = "boundary-gamma",
                    MonitorBrightnessMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 0,
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 100
                    },
                    MonitorGammaMap = new Dictionary<string, int>
                    {
                        ["\\\\?\\DISPLAY#DEL41AB#5&abc"] = 0,
                        ["\\\\?\\DISPLAY#GSM59AB#7&def"] = 100
                    }
                }
            }
        }, SettingsStore.SerializerOptions);

        File.WriteAllText(_tempFile, json);
        var store = new SettingsStore(_tempFile);

        // Act
        var settings = store.Load();

        // Assert: both boundary values preserved
        settings.Profiles.Should().HaveCount(1);
        settings.Profiles[0].MonitorGammaMap.Should().NotBeNull();
        settings.Profiles[0].MonitorGammaMap!["\\\\?\\DISPLAY#DEL41AB#5&abc"].Should().Be(0);
        settings.Profiles[0].MonitorGammaMap!["\\\\?\\DISPLAY#GSM59AB#7&def"].Should().Be(100);
    }
}
