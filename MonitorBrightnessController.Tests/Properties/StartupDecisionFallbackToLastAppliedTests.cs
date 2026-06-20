using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using MonitorBrightnessController.Models;
using MonitorBrightnessController.Presentation;

namespace MonitorBrightnessController.Tests.Properties;

// Feature: enhancements-v1-5, Property 3: Startup decision falls back to LastAppliedProfileName

/// <summary>
/// Property-based tests verifying that StartupCoordinator.Decide falls back to
/// LastAppliedProfileName when DefaultStartupProfileName is null or empty.
/// </summary>
/// <remarks>
/// **Validates: Requirements 2.3**
/// </remarks>
public class StartupDecisionFallbackToLastAppliedTests
{
    /// <summary>
    /// Property 3: Startup decision falls back to LastAppliedProfileName.
    /// For any AppSettings with AutoApplyOnStartup=true, DefaultStartupProfileName null or empty,
    /// and a non-null, non-empty LastAppliedProfileName that exists in the provided profile name list,
    /// StartupCoordinator.Decide shall return StartupAction.ApplyLastProfile with that profile name.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Decide_FallsBackToLastAppliedProfileName_WhenDefaultIsNullOrEmpty()
    {
        var allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        // Generate a non-empty profile name for LastAppliedProfileName
        var nonEmptyProfileNameGen = Gen.Choose(1, 20)
            .SelectMany(len =>
                Gen.ArrayOf(len, Gen.Elements(allowedChars))
                    .Select(chars => new string(chars)));

        // Generate additional profile names for the list (0-5 extra names)
        var extraNamesGen = Gen.Choose(0, 5)
            .SelectMany(count => Gen.ListOf(count, nonEmptyProfileNameGen).Select(l => l.ToList()));

        // DefaultStartupProfileName should be null or empty
        var nullOrEmptyGen = Gen.Elements<string?>(null, string.Empty);

        var inputGen =
            from lastAppliedName in nonEmptyProfileNameGen
            from defaultProfileName in nullOrEmptyGen
            from extraNames in extraNamesGen
            select new { LastAppliedName = lastAppliedName, DefaultProfileName = defaultProfileName, ExtraNames = extraNames };

        return Prop.ForAll(Arb.From(inputGen), input =>
        {
            // Build profile list that includes the LastAppliedProfileName
            var profileNames = new List<string>(input.ExtraNames) { input.LastAppliedName };

            var settings = new AppSettings
            {
                AutoApplyOnStartup = true,
                DefaultStartupProfileName = input.DefaultProfileName,
                LastAppliedProfileName = input.LastAppliedName
            };

            var decision = StartupCoordinator.Decide(settings, profileNames, isCliOverride: false);

            decision.Action.Should().Be(StartupAction.ApplyLastProfile,
                "when AutoApplyOnStartup is true and DefaultStartupProfileName is null/empty, " +
                "Decide should fall back to LastAppliedProfileName");
            decision.ProfileName.Should().Be(input.LastAppliedName,
                "the returned profile name should match the LastAppliedProfileName");
        });
    }
}
