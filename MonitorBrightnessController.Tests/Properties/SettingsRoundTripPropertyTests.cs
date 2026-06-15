using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-4, Property 6: AppSettings CheckForUpdatesOnStartup round-trip

/// <summary>
/// Property-based tests verifying that AppSettings round-trips through JSON serialization
/// preserve the CheckForUpdatesOnStartup value.
/// </summary>
public class SettingsRoundTripPropertyTests
{
    /// <summary>
    /// Property 6: For any AppSettings instance with CheckForUpdatesOnStartup set to either
    /// true or false, serializing to JSON and deserializing back produces an AppSettings with
    /// the same CheckForUpdatesOnStartup value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 6.1**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property CheckForUpdatesOnStartup_RoundTrip_PreservesValue()
    {
        var profileNameGen = Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray())
            .ArrayOf(Gen.Choose(3, 10).Sample(0, 1).Single())
            .Select(c => new string(c));

        var profileGen =
            from name in profileNameGen
            from monitorCount in Gen.Choose(0, 3)
            from brightnessValues in Gen.ArrayOf(monitorCount, Gen.Choose(0, 100))
            let paths = Enumerable.Range(0, monitorCount)
                .Select(i => $"\\\\?\\DISPLAY#MON{i}#path{i}")
                .ToArray()
            select new Profile
            {
                Name = name,
                MonitorBrightnessMap = paths.Zip(brightnessValues)
                    .ToDictionary(p => p.First, p => p.Second),
                MonitorGammaMap = null
            };

        var appSettingsGen =
            from checkForUpdates in Arb.Generate<bool>()
            from autoApply in Arb.Generate<bool>()
            from minimizeToTray in Arb.Generate<bool>()
            from smoothTransition in Arb.Generate<bool>()
            from startWithWindows in Arb.Generate<bool>()
            from refreshOnFocus in Arb.Generate<bool>()
            from transitionDuration in Gen.Choose(100, 2000)
            from profileCount in Gen.Choose(0, 3)
            from profiles in Gen.ListOf(profileCount, profileGen)
            select new AppSettings
            {
                CheckForUpdatesOnStartup = checkForUpdates,
                AutoApplyOnStartup = autoApply,
                MinimizeToTray = minimizeToTray,
                SmoothTransition = smoothTransition,
                StartWithWindows = startWithWindows,
                RefreshOnFocus = refreshOnFocus,
                TransitionDurationMs = transitionDuration,
                Profiles = profiles.ToList(),
                LastAppliedProfileName = null,
                DefaultStartupProfileName = null
            };

        return Prop.ForAll(Arb.From(appSettingsGen), settings =>
        {
            // Serialize to JSON
            string json = JsonSerializer.Serialize(settings, SettingsStore.SerializerOptions);

            // Deserialize back
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

            // Verify CheckForUpdatesOnStartup is preserved
            deserialized.Should().NotBeNull();
            deserialized!.CheckForUpdatesOnStartup.Should().Be(settings.CheckForUpdatesOnStartup,
                "CheckForUpdatesOnStartup must survive JSON round-trip serialization");
        });
    }
}
