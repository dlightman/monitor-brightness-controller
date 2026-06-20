using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-5, Property 5: Startup decision detects missing default profile

/// <summary>
/// Property-based tests verifying that StartupCoordinator.Decide returns
/// DefaultProfileMissing with a non-null notice when the configured DefaultStartupProfileName
/// does not exist in the provided profile name list.
/// </summary>
/// <remarks>
/// **Validates: Requirements 2.8**
/// </remarks>
public class StartupDecisionMissingDefaultProfileTests
{
    /// <summary>
    /// Property 5: Startup decision detects missing default profile.
    /// For any AppSettings with AutoApplyOnStartup=true and a non-null, non-empty
    /// DefaultStartupProfileName that does NOT exist in the provided profile name list,
    /// StartupCoordinator.Decide shall return StartupAction.DefaultProfileMissing with a non-null notice.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Decide_DetectsMissingDefaultProfile_WhenDefaultDoesNotExist()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        // Generate a non-empty profile name for DefaultStartupProfileName
        var nonEmptyProfileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        // Generate additional profile names for the existing list (0-5 names)
        var existingNamesGen = Gen.Choose(0, 5)
            .SelectMany(count => Gen.ListOf(count, nonEmptyProfileNameGen).Select(l => l.ToList()));

        var inputGen =
            from defaultProfileName in nonEmptyProfileNameGen
            from existingNames in existingNamesGen
            select new { DefaultProfileName = defaultProfileName, ExistingNames = existingNames };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Ensure the DefaultStartupProfileName does NOT exist in the profile list
            // by removing any case-insensitive matches
            var profileNames = input.ExistingNames
                .Where(name => !string.Equals(name, input.DefaultProfileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var settings = new AppSettings
            {
                AutoApplyOnStartup = true,
                DefaultStartupProfileName = input.DefaultProfileName
            };

            var decision = StartupCoordinator.Decide(settings, profileNames, isCliOverride: false);

            decision.Action.Should().Be(StartupAction.DefaultProfileMissing,
                "when AutoApplyOnStartup is true and DefaultStartupProfileName does not exist " +
                "in the profile list, Decide should return DefaultProfileMissing");
            decision.Notice.Should().NotBeNull(
                "when the default profile is missing, Decide should return a non-null notice");
        });
    }
}
