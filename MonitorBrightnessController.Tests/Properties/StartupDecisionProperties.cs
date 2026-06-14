using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Infrastructure;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: startup-and-install-enhancements, Property 4: Startup decision correctness
// Feature: startup-and-install-enhancements, Property 3: Settings round-trip preserves DefaultStartupProfileName

/// <summary>
/// Custom arbitraries for startup decision property tests.
/// </summary>
public static class StartupDecisionArbitraries
{
    /// <summary>
    /// Generates a valid profile name (1-20 chars, alphanumeric + underscore).
    /// </summary>
    public static Arbitrary<string> ProfileName()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();
        var gen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));
        return Arb.From(gen);
    }

    /// <summary>
    /// Generates a list of valid profile names (0-10 items).
    /// </summary>
    public static Arbitrary<List<string>> ProfileNameList()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();
        var nameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));
        var gen = Gen.Choose(0, 10)
            .SelectMany(count => Gen.ListOf(count, nameGen).Select(l => l.ToList()));
        return Arb.From(gen);
    }
}

/// <summary>
/// Property-based tests for StartupCoordinator.Decide covering the default startup profile logic.
/// Validates Requirements 2.4, 2.5, 2.6.
/// </summary>
public class StartupDecisionProperties
{
    /// <summary>
    /// Property 4: Startup decision correctness.
    /// For any combination of (DefaultStartupProfileName, list of existing profile names, CLI override flag),
    /// StartupCoordinator.Decide SHALL:
    /// - Skip profile application when CLI override is true regardless of other inputs;
    /// - Apply the named profile when it exists in the profile list and CLI override is false;
    /// - Produce a "missing profile" decision with notice containing the profile name when the named profile
    ///   does not exist in the profile list and CLI override is false;
    /// - Return AutoApplyDisabled when DefaultStartupProfileName is null/empty and not CLI override
    ///   (with AutoApplyOnStartup = false).
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.4, 2.5, 2.6**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property StartupDecision_IsCorrectForAllInputCombinations()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        var profileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        var profileListGen = Gen.Choose(0, 10)
            .SelectMany(count => Gen.ListOf(count, profileNameGen).Select(l => l.ToList()));

        // Generate a nullable default startup profile name: null, empty, or a valid name
        var defaultProfileGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(string.Empty),
            profileNameGen.Select<string, string?>(n => n));

        var inputGen =
            from defaultProfile in defaultProfileGen
            from profileList in profileListGen
            from isCliOverride in Arb.Generate<bool>()
            select new { DefaultProfile = defaultProfile, ProfileList = profileList, IsCliOverride = isCliOverride };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            var settings = new AppSettings
            {
                DefaultStartupProfileName = input.DefaultProfile,
                AutoApplyOnStartup = false // Keep focused on default profile logic
            };

            var decision = StartupCoordinator.Decide(settings, input.ProfileList, input.IsCliOverride);

            if (input.IsCliOverride)
            {
                // Requirement 2.5: CLI override skips all startup profile application
                decision.Action.Should().Be(StartupAction.CliOverride,
                    "CLI override should always result in CliOverride action regardless of other inputs");
            }
            else if (!string.IsNullOrEmpty(input.DefaultProfile))
            {
                bool profileExists = input.ProfileList.Any(name =>
                    string.Equals(name, input.DefaultProfile, StringComparison.OrdinalIgnoreCase));

                if (profileExists)
                {
                    // Requirement 2.4: apply the configured default startup profile
                    decision.Action.Should().Be(StartupAction.ApplyDefaultProfile,
                        "when DefaultStartupProfileName is set and exists, action should be ApplyDefaultProfile");
                    decision.ProfileName.Should().Be(input.DefaultProfile,
                        "the profile name in the decision should match the configured default");
                }
                else
                {
                    // Requirement 2.6: configured profile doesn't exist — warn and skip
                    decision.Action.Should().Be(StartupAction.DefaultProfileMissing,
                        "when DefaultStartupProfileName is set but not found, action should be DefaultProfileMissing");
                    decision.ProfileName.Should().Be(input.DefaultProfile,
                        "the profile name in the decision should match the configured default");
                    decision.Notice.Should().NotBeNull()
                        .And.Contain(input.DefaultProfile,
                            "the notice should contain the missing profile name");
                }
            }
            else
            {
                // DefaultStartupProfileName is null or empty, AutoApplyOnStartup is false
                decision.Action.Should().Be(StartupAction.AutoApplyDisabled,
                    "when no default profile is set and AutoApplyOnStartup is false, action should be AutoApplyDisabled");
            }
        });
    }

    /// <summary>
    /// Property 3: Settings round-trip preserves DefaultStartupProfileName.
    /// For any valid AppSettings with a random DefaultStartupProfileName (null or a valid
    /// profile name string of 1-20 characters), serializing to JSON and deserializing back produces an
    /// equivalent AppSettings with the same DefaultStartupProfileName value.
    /// </summary>
    /// <remarks>
    /// **Validates: Requirements 2.1**
    /// </remarks>
    [Property(MaxTest = 100)]
    public Property SettingsRoundTrip_PreservesDefaultStartupProfileName()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

        var validNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        // Generate either null or a valid profile name string
        var profileNameGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            validNameGen.Select<string, string?>(s => s));

        return Prop.ForAll(Arb.From(profileNameGen), profileName =>
        {
            // Create AppSettings with the generated DefaultStartupProfileName (other fields at defaults)
            var settings = new AppSettings
            {
                DefaultStartupProfileName = profileName
            };

            // Serialize using SettingsStore.SerializerOptions
            string json = JsonSerializer.Serialize(settings, SettingsStore.SerializerOptions);

            // Deserialize using SettingsStore.SerializerOptions
            var deserialized = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.SerializerOptions);

            // Assert the deserialized DefaultStartupProfileName equals the original
            deserialized.Should().NotBeNull();
            deserialized!.DefaultStartupProfileName.Should().Be(profileName,
                "serializing and deserializing AppSettings should preserve DefaultStartupProfileName");
        });
    }
}
