using System.Text.Json;
using FluentAssertions;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;
using Xunit;

namespace MonitorBrightnessController.Tests;

/// <summary>
/// Unit tests verifying that <see cref="AppSettings"/> deserialization handles
/// the <c>CheckForUpdatesOnStartup</c> property correctly, including the default
/// value behavior for new installs and upgrades.
/// </summary>
public class AppSettingsDeserializationTests
{
    /// <summary>
    /// When the JSON does not contain the CheckForUpdatesOnStartup property (upgrade scenario),
    /// deserialization should default to true.
    /// Validates: Requirements 6.4, 6.5
    /// </summary>
    [Fact]
    public void Deserialize_MissingCheckForUpdatesOnStartup_DefaultsToTrue()
    {
        // JSON with no CheckForUpdatesOnStartup property — simulates an upgrade from
        // a version that didn't have this setting.
        var json = """
            {
                "profiles": [],
                "autoApplyOnStartup": false,
                "minimizeToTray": true,
                "startWithWindows": false
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

        settings.Should().NotBeNull();
        settings!.CheckForUpdatesOnStartup.Should().BeTrue();
    }

    /// <summary>
    /// When the JSON explicitly sets CheckForUpdatesOnStartup to false,
    /// deserialization should preserve the false value.
    /// Validates: Requirements 6.4, 6.5
    /// </summary>
    [Fact]
    public void Deserialize_ExplicitFalse_PreservesFalse()
    {
        var json = """
            {
                "profiles": [],
                "autoApplyOnStartup": false,
                "checkForUpdatesOnStartup": false
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

        settings.Should().NotBeNull();
        settings!.CheckForUpdatesOnStartup.Should().BeFalse();
    }

    /// <summary>
    /// When the JSON explicitly sets CheckForUpdatesOnStartup to true,
    /// deserialization should preserve the true value.
    /// Validates: Requirements 6.4, 6.5
    /// </summary>
    [Fact]
    public void Deserialize_ExplicitTrue_PreservesTrue()
    {
        var json = """
            {
                "profiles": [],
                "autoApplyOnStartup": false,
                "checkForUpdatesOnStartup": true
            }
            """;

        var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

        settings.Should().NotBeNull();
        settings!.CheckForUpdatesOnStartup.Should().BeTrue();
    }
}
